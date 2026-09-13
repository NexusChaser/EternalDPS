using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NexusChaser.EternalDPS.Abstractions;

namespace NexusChaser.EternalDPS.Stores
{
    /// <summary>
    /// A store backed by a folder on disk. The one used on desktop, Android and iOS.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lives in the core, not in the engine layer, because it needs nothing but
    /// <see cref="System.IO"/>. That keeps it usable from the command line tools and testable in CI
    /// without opening Unity, which is the property the whole package is arranged around. The engine
    /// layer supplies only the root path.
    /// </para>
    /// <para>
    /// Every path a caller hands in is resolved and checked against the root. A record id cannot
    /// escape on its own — <see cref="EternalKey"/> sees to that — but a path can also arrive from a
    /// file already on disk, and that one has been through no validation at all.
    /// </para>
    /// </remarks>
    public sealed class FileStore : IStore
    {
        /// <summary>
        /// Extension of the temporary file a write goes through before replacing the real one.
        /// </summary>
        /// <remarks>
        /// Deliberately not <c>.etm</c>. Steam's Auto-Cloud is configured with a pattern, and the
        /// obvious pattern is <c>*.etm</c>; a half-written temporary file matching it would be
        /// uploaded to the cloud as if it were a save. It also keeps a crashed write from looking
        /// like a real record to anything that scans the folder.
        /// </remarks>
        public const string PartialExtension = ".part";

        private readonly string _root;

        /// <summary>Creates a store over a folder, creating it if it is not there.</summary>
        /// <param name="rootDirectory">Absolute path of the folder that holds the records.</param>
        /// <exception cref="ArgumentException">The path is missing.</exception>
        public FileStore(string rootDirectory)
        {
            if (string.IsNullOrEmpty(rootDirectory))
            {
                throw new ArgumentException("A file store needs a root directory.", nameof(rootDirectory));
            }

            _root = Path.GetFullPath(rootDirectory);

            Directory.CreateDirectory(_root);
        }

        /// <summary>The folder this store writes into.</summary>
        public string RootDirectory => _root;

        /// <summary>
        /// Everything a filesystem offers. A desktop disk is the best case and the one the other
        /// stores degrade from.
        /// </summary>
        public StoreCapabilities Capabilities => StoreCapabilities.All;

        /// <inheritdoc />
        public Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken)
        {
            return Task.FromResult(File.Exists(Resolve(relativePath)));
        }

        /// <inheritdoc />
        public async Task<byte[]> ReadAsync(string relativePath, CancellationToken cancellationToken)
        {
            var full = Resolve(relativePath);

            if (!File.Exists(full))
            {
                return null;
            }

            using (var stream = new FileStream(
                full, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true))
            {
                var length = (int)stream.Length;
                var buffer = new byte[length];
                var read = 0;

                while (read < length)
                {
                    var got = await stream.ReadAsync(buffer, read, length - read, cancellationToken)
                        .ConfigureAwait(false);

                    if (got == 0)
                    {
                        // The file shrank while it was being read. Reporting a short buffer as if it
                        // were the record would hand the caller a truncated container to puzzle over.
                        throw new IOException(
                            "The file ended after " + read + " of " + length + " bytes while being read.");
                    }

                    read += got;
                }

                return buffer;
            }
        }

        /// <summary>
        /// Writes a record, replacing whatever was there, without ever leaving a half-written file
        /// in its place.
        /// </summary>
        /// <remarks>
        /// The bytes go to a temporary file first and only then take the record's place, in one
        /// filesystem operation. Writing straight over the real file would mean that losing power
        /// partway leaves a file that exists, is the right name, and is half a save — which is worse
        /// than no save at all, because the game will try to load it.
        /// </remarks>
        public async Task WriteAsync(string relativePath, byte[] data, CancellationToken cancellationToken)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            var full = Resolve(relativePath);
            var directory = Path.GetDirectoryName(full);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var partial = full + PartialExtension;

            try
            {
                using (var stream = new FileStream(
                    partial, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
                {
                    await stream.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                Replace(partial, full);
            }
            catch
            {
                TryDelete(partial);
                throw;
            }
        }

        /// <inheritdoc />
        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken)
        {
            var full = Resolve(relativePath);

            // Deleting something that is not there is not a failure: the caller wanted it gone and
            // it is gone. Making this throw would force a check-then-delete race on every caller.
            TryDelete(full);

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken cancellationToken)
        {
            var results = new List<string>();

            if (Directory.Exists(_root))
            {
                foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // A temporary file is a write in progress or the debris of one that died. It is
                    // not a record and must never be offered as one.
                    if (file.EndsWith(PartialExtension, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var relative = ToRelative(file);

                    if (string.IsNullOrEmpty(prefix) || relative.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        results.Add(relative);
                    }
                }
            }

            results.Sort(StringComparer.Ordinal);

            return Task.FromResult((IReadOnlyList<string>)results);
        }

        /// <summary>
        /// Nothing to do: a filesystem write has already landed once the handle is closed. The
        /// browser store is the one that needs this.
        /// </summary>
        public Task FlushAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Removes any temporary files left behind by a write that never finished.
        /// </summary>
        /// <returns>How many were removed.</returns>
        public int CleanPartialFiles()
        {
            if (!Directory.Exists(_root))
            {
                return 0;
            }

            var removed = 0;

            foreach (var file in Directory.EnumerateFiles(_root, "*" + PartialExtension, SearchOption.AllDirectories))
            {
                if (TryDelete(file))
                {
                    removed++;
                }
            }

            return removed;
        }

        /// <summary>
        /// Puts the temporary file in the record's place, in one operation wherever the platform
        /// offers one.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>File.Move</c> with an overwrite flag would be the obvious call and it is not available
        /// here: Unity's .NET Standard profile only carries the two-argument overload, which refuses
        /// to overwrite. <c>File.Replace</c> is the primitive that does exist and that the operating
        /// system performs as a single step.
        /// </para>
        /// <para>
        /// Some filesystems do not support it — external storage on Android formatted as FAT32, for
        /// one. There the write degrades to remove-then-rename, which leaves a brief window where
        /// neither file is in place. That window is exactly what backups are for, and it is why
        /// atomicity is declared as a capability instead of promised.
        /// </para>
        /// </remarks>
        private static void Replace(string partial, string full)
        {
            if (!File.Exists(full))
            {
                File.Move(partial, full);
                return;
            }

            try
            {
                File.Replace(partial, full, destinationBackupFileName: null);
            }
            catch (PlatformNotSupportedException)
            {
                Degrade(partial, full);
            }
            catch (IOException)
            {
                Degrade(partial, full);
            }
        }

        private static void Degrade(string partial, string full)
        {
            File.Delete(full);
            File.Move(partial, full);
        }

        /// <summary>
        /// Turns a relative path into a real one, refusing anything that would land outside the root.
        /// </summary>
        /// <exception cref="ArgumentException">The path is empty, rooted, or escapes the store.</exception>
        private string Resolve(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                throw new ArgumentException("A record path cannot be empty.", nameof(relativePath));
            }

            if (Path.IsPathRooted(relativePath))
            {
                throw new ArgumentException(
                    "'" + relativePath + "' is an absolute path. A store addresses records relative to its root.",
                    nameof(relativePath));
            }

            var combined = Path.GetFullPath(Path.Combine(_root, relativePath));

            // Compared after resolving, so that a path built out of ".." segments is caught by where
            // it actually lands rather than by how it is spelled.
            if (!combined.StartsWith(WithSeparator(_root), StringComparison.Ordinal) &&
                !string.Equals(combined, _root, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "'" + relativePath + "' resolves outside the store's root directory.", nameof(relativePath));
            }

            return combined;
        }

        private string ToRelative(string fullPath)
        {
            var relative = fullPath.Substring(WithSeparator(_root).Length);

            // The rest of the system speaks in forward slashes on every platform, because the same
            // path has to mean the same record on Windows, on Linux and inside Steam Cloud.
            return relative.Replace(Path.DirectorySeparatorChar, '/').Replace('\\', '/');
        }

        private static string WithSeparator(string directory)
        {
            return directory.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? directory
                : directory + Path.DirectorySeparatorChar;
        }

        private static bool TryDelete(string fullPath)
        {
            try
            {
                if (!File.Exists(fullPath))
                {
                    return false;
                }

                File.Delete(fullPath);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
