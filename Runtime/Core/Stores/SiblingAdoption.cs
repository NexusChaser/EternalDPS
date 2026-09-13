using System;
using System.Collections.Generic;
using System.IO;
using NexusChaser.EternalDPS.Abstractions;

namespace NexusChaser.EternalDPS.Stores
{
    /// <summary>How an adoption sweep ended.</summary>
    public enum AdoptionOutcome
    {
        /// <summary>The current folder already holds saves. Nothing to recover.</summary>
        NotNeeded = 0,

        /// <summary>A sweep already ran here and left its mark. It does not run again.</summary>
        AlreadyDone = 1,

        /// <summary>The sweep ran and found nothing this game could read.</summary>
        NothingFound = 2,

        /// <summary>Saves were found in a sibling folder and copied across.</summary>
        Adopted = 3,
    }

    /// <summary>What a sweep did, and what it looked at.</summary>
    public sealed class AdoptionResult
    {
        internal AdoptionResult(
            AdoptionOutcome outcome,
            string sourceDirectory,
            IReadOnlyList<string> adoptedFiles,
            int directoriesExamined,
            int filesExamined,
            DateTimeOffset newestSavedAtUtc)
        {
            Outcome = outcome;
            SourceDirectory = sourceDirectory;
            AdoptedFiles = adoptedFiles ?? new string[0];
            DirectoriesExamined = directoriesExamined;
            FilesExamined = filesExamined;
            NewestSavedAtUtc = newestSavedAtUtc;
        }

        /// <summary>How it ended.</summary>
        public AdoptionOutcome Outcome { get; }

        /// <summary>Where the adopted saves came from. Null when nothing was adopted.</summary>
        public string SourceDirectory { get; }

        /// <summary>The records copied, as store-relative paths.</summary>
        public IReadOnlyList<string> AdoptedFiles { get; }

        /// <summary>How many sibling folders were looked at.</summary>
        public int DirectoriesExamined { get; }

        /// <summary>How many candidate files were opened.</summary>
        public int FilesExamined { get; }

        /// <summary>Timestamp of the newest save that was adopted.</summary>
        public DateTimeOffset NewestSavedAtUtc { get; }

        /// <summary>True when something was actually recovered.</summary>
        public bool DidAdopt => Outcome == AdoptionOutcome.Adopted;

        public override string ToString()
        {
            return DidAdopt
                ? "Adopted " + AdoptedFiles.Count + " record(s) from " + SourceDirectory
                : Outcome.ToString();
        }
    }

    /// <summary>
    /// Recovers saves left behind in a neighbouring folder after the game was renamed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Save folders are built from the company and product names, so renaming either sends the
    /// engine to a fresh, empty folder while the player's saves sit in the old one. The product
    /// identity inside each file is what makes them findable again; this is what goes and looks.
    /// </para>
    /// <para>
    /// <strong>The sweep is limited to sibling folders</strong> — the ones next to the current save
    /// folder, and nothing else. Walking the disk looking for files that might belong to us is slow,
    /// invasive, and would read files the player never meant this game to open.
    /// </para>
    /// <para>
    /// <strong>Files are copied, never moved.</strong> The originals stay where they are as a safety
    /// net, because an adoption that goes wrong must not be the thing that destroys the only copy.
    /// </para>
    /// </remarks>
    public static class SiblingAdoption
    {
        /// <summary>
        /// Name of the file left behind so the sweep does not run on every launch.
        /// </summary>
        /// <remarks>
        /// Not a <c>.etm</c>, so that a cloud pattern of <c>*.etm</c> does not sweep it up — this is
        /// a fact about this device, not part of the player's saves.
        /// </remarks>
        public const string MarkerFileName = "adoption.marker";

        /// <summary>Largest candidate file that will be opened, in bytes.</summary>
        public const int MaxCandidateBytes = 32 * 1024 * 1024;

        /// <summary>
        /// Looks for adoptable saves beside the store's folder and copies the best set across.
        /// </summary>
        /// <param name="store">The store for this game. Its root is where saves are copied to.</param>
        /// <param name="identity">Who we are, and who we also answer to.</param>
        /// <param name="currentSchemaVersion">The schema this build writes.</param>
        /// <param name="log">Where the sweep reports what it did.</param>
        /// <param name="hasMigrationPath">Optional. Asked whether an older schema can be brought forward.</param>
        public static AdoptionResult TryAdopt(
            FileStore store,
            ProductIdentity identity,
            uint currentSchemaVersion,
            IEternalLog log = null,
            Func<uint, bool> hasMigrationPath = null)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }

            log = log ?? NullLog.Instance;

            var root = new DirectoryInfo(store.RootDirectory);
            var markerPath = Path.Combine(root.FullName, MarkerFileName);

            if (File.Exists(markerPath))
            {
                return new AdoptionResult(AdoptionOutcome.AlreadyDone, null, null, 0, 0, default);
            }

            if (HasAnyRecord(root))
            {
                // There are saves here already. Copying older ones on top of them would be the
                // opposite of a rescue.
                return new AdoptionResult(AdoptionOutcome.NotNeeded, null, null, 0, 0, default);
            }

            var parent = root.Parent;

            if (parent == null || !parent.Exists)
            {
                return new AdoptionResult(AdoptionOutcome.NothingFound, null, null, 0, 0, default);
            }

            string bestDirectory = null;
            List<string> bestFiles = null;
            var bestTimestamp = DateTimeOffset.MinValue;
            var directoriesExamined = 0;
            var filesExamined = 0;

            foreach (var sibling in parent.GetDirectories())
            {
                if (string.Equals(sibling.FullName, root.FullName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                directoriesExamined++;

                var found = new List<string>();
                var newest = DateTimeOffset.MinValue;

                foreach (var file in EnumerateCandidates(sibling))
                {
                    filesExamined++;

                    var report = Inspect(file, identity, currentSchemaVersion, hasMigrationPath);

                    if (report == null || !report.IsAdoptable)
                    {
                        continue;
                    }

                    found.Add(file.FullName);

                    if (report.SavedAtUtc > newest)
                    {
                        newest = report.SavedAtUtc;
                    }
                }

                if (found.Count == 0)
                {
                    continue;
                }

                log.Info(
                    "Found " + found.Count + " adoptable record(s) in '" + sibling.Name +
                    "', newest " + newest.UtcDateTime.ToString("u") + ".");

                // Several old folders can pile up across renames. The most recently played one is
                // the one the player would expect back.
                if (newest <= bestTimestamp)
                {
                    continue;
                }

                bestTimestamp = newest;
                bestDirectory = sibling.FullName;
                bestFiles = found;
            }

            if (bestDirectory == null)
            {
                return new AdoptionResult(
                    AdoptionOutcome.NothingFound, null, null, directoriesExamined, filesExamined, default);
            }

            var copied = Copy(bestFiles, bestDirectory, root.FullName);

            File.WriteAllText(
                markerPath,
                "Adopted from: " + bestDirectory + Environment.NewLine +
                "On: " + DateTimeOffset.UtcNow.UtcDateTime.ToString("u") + Environment.NewLine +
                "Records: " + copied.Count + Environment.NewLine +
                "The originals were copied, not moved, and are still where they were." + Environment.NewLine);

            // The player has to be told their data was recovered. Never in silence.
            log.Warning(
                "Recovered " + copied.Count + " save(s) from '" + Path.GetFileName(bestDirectory) +
                "'. The originals were left untouched. The player should be told their data was brought across.");

            return new AdoptionResult(
                AdoptionOutcome.Adopted, bestDirectory, copied, directoriesExamined, filesExamined, bestTimestamp);
        }

        /// <summary>
        /// Inspects one file without adopting it, for a tool that needs to explain why a save was
        /// or was not considered ours.
        /// </summary>
        public static AdoptionReport Inspect(
            FileInfo file,
            ProductIdentity identity,
            uint currentSchemaVersion,
            Func<uint, bool> hasMigrationPath = null)
        {
            try
            {
                if (file.Length > MaxCandidateBytes)
                {
                    return null;
                }

                return Adoption.CanAdopt(
                    File.ReadAllBytes(file.FullName), identity, currentSchemaVersion, hasMigrationPath);
            }
            catch (IOException)
            {
                // A file being written by something else, or one we cannot open. Not our business.
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static IEnumerable<FileInfo> EnumerateCandidates(DirectoryInfo directory)
        {
            FileInfo[] files;

            try
            {
                files = directory.GetFiles("*" + EternalPackage.FileExtension, SearchOption.AllDirectories);
            }
            catch (UnauthorizedAccessException)
            {
                // A neighbouring folder we are not allowed into is not an error; it is just not ours.
                yield break;
            }
            catch (IOException)
            {
                yield break;
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }

        private static bool HasAnyRecord(DirectoryInfo root)
        {
            if (!root.Exists)
            {
                return false;
            }

            return root.GetFiles("*" + EternalPackage.FileExtension, SearchOption.AllDirectories).Length > 0;
        }

        private static IReadOnlyList<string> Copy(IReadOnlyList<string> files, string sourceRoot, string targetRoot)
        {
            var copied = new List<string>();

            foreach (var file in files)
            {
                // The layout inside the old folder is the layout we want here: machine, account and
                // slot folders mean the same thing on both sides.
                var relative = file.Substring(sourceRoot.Length).TrimStart(Path.DirectorySeparatorChar, '/', '\\');
                var destination = Path.Combine(targetRoot, relative);
                var directory = Path.GetDirectoryName(destination);

                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.Copy(file, destination, overwrite: false);

                copied.Add(relative.Replace(Path.DirectorySeparatorChar, '/').Replace('\\', '/'));
            }

            return copied;
        }
    }
}
