using System;
using NexusChaser.EternalDPS.Container;

namespace NexusChaser.EternalDPS
{
    /// <summary>Whether a file found somewhere else can be taken as ours.</summary>
    public enum AdoptionVerdict
    {
        /// <summary>It is ours and this build can read it.</summary>
        Adoptable = 0,

        /// <summary>Not one of our files at all. The commonest answer when scanning a folder.</summary>
        NotEternalFile = 1,

        /// <summary>Ours, but written by a newer build. Leave it alone.</summary>
        ContainerTooNew = 2,

        /// <summary>A valid container belonging to a different game.</summary>
        ForeignProduct = 3,

        /// <summary>Ours, but its schema is newer than this build understands.</summary>
        SchemaTooNew = 4,

        /// <summary>Ours and older, but there is no migration path from that schema.</summary>
        NoMigrationPath = 5,

        /// <summary>It claims to be a container and its structure does not hold up.</summary>
        Malformed = 6,
    }

    /// <summary>
    /// The answer to "can I adopt this file?", with the reason when the answer is no.
    /// </summary>
    /// <remarks>
    /// The reason is the part that earns its keep. A tool that can only say "incompatible" leaves
    /// whoever is looking at an orphaned save with nowhere to go; one that says "this is product
    /// X and we are product Y" tells them exactly what happened.
    /// </remarks>
    public sealed class AdoptionReport
    {
        private AdoptionReport(
            AdoptionVerdict verdict,
            string reason,
            Guid productId,
            uint schemaVersion,
            DateTimeOffset savedAtUtc)
        {
            Verdict = verdict;
            Reason = reason;
            ProductId = productId;
            SchemaVersion = schemaVersion;
            SavedAtUtc = savedAtUtc;
        }

        /// <summary>The answer.</summary>
        public AdoptionVerdict Verdict { get; }

        /// <summary>Why, in a sentence, when the answer is no.</summary>
        public string Reason { get; }

        /// <summary>Which product wrote it, when that was readable.</summary>
        public Guid ProductId { get; }

        /// <summary>Its schema version, when that was readable.</summary>
        public uint SchemaVersion { get; }

        /// <summary>
        /// When it was written. Used to pick between several adoptable candidates: the most recent
        /// one wins.
        /// </summary>
        public DateTimeOffset SavedAtUtc { get; }

        /// <summary>True only for <see cref="AdoptionVerdict.Adoptable"/>.</summary>
        public bool IsAdoptable => Verdict == AdoptionVerdict.Adoptable;

        internal static AdoptionReport Yes(Guid productId, uint schemaVersion, DateTimeOffset savedAtUtc)
        {
            return new AdoptionReport(AdoptionVerdict.Adoptable, null, productId, schemaVersion, savedAtUtc);
        }

        internal static AdoptionReport No(
            AdoptionVerdict verdict,
            string reason,
            Guid productId = default,
            uint schemaVersion = 0,
            DateTimeOffset savedAtUtc = default)
        {
            return new AdoptionReport(verdict, reason, productId, schemaVersion, savedAtUtc);
        }

        public override string ToString()
        {
            return IsAdoptable ? "Adoptable" : Verdict + ": " + Reason;
        }
    }

    /// <summary>
    /// Decides whether a file found elsewhere belongs to this game and can be read.
    /// </summary>
    public static class Adoption
    {
        /// <summary>
        /// Interrogates a file, cheapest check first, without ever deserialising the body.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The order is magic, container version, product identity, migration path. Each stage is
        /// more expensive than the last and none of them needs the body, the game's serialiser, or
        /// a key — which is the whole reason the metadata sits outside the body and unencrypted.
        /// Scanning a folder full of other applications' files costs almost nothing, because
        /// nearly all of them fail on the first four bytes.
        /// </para>
        /// <para>
        /// The signature is deliberately not checked here. A file from the game's own previous
        /// folder is signed with the same key and will verify later, at load; refusing to even
        /// consider it at this stage would mean a key rotation quietly made old saves unadoptable.
        /// </para>
        /// </remarks>
        /// <param name="bytes">The candidate file.</param>
        /// <param name="identity">Who we are, and who we also answer to.</param>
        /// <param name="currentSchemaVersion">The schema this build writes.</param>
        /// <param name="hasMigrationPath">
        /// Optional. Asked whether an older schema can be brought forward. When omitted, every
        /// schema at or below the current one is assumed reachable.
        /// </param>
        public static AdoptionReport CanAdopt(
            byte[] bytes,
            ProductIdentity identity,
            uint currentSchemaVersion,
            Func<uint, bool> hasMigrationPath = null)
        {
            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }

            // 1. Is it one of ours at all?
            if (!ContainerReader.TryReadPreamble(bytes, out var preamble, out var failure))
            {
                switch (failure.Status)
                {
                    case IntegrityStatus.MagicMismatch:
                        return AdoptionReport.No(
                            AdoptionVerdict.NotEternalFile,
                            "The file does not start with the container magic.");

                    // 2. Ours, but from a build that knows something we do not.
                    case IntegrityStatus.UnsupportedVersion:
                        return AdoptionReport.No(
                            AdoptionVerdict.ContainerTooNew,
                            failure.Detail);

                    default:
                        return AdoptionReport.No(AdoptionVerdict.Malformed, failure.Detail);
                }
            }

            if (!preamble.HasMetadata)
            {
                return AdoptionReport.No(
                    AdoptionVerdict.Malformed,
                    "The container carries no metadata, so there is no way to tell whose it is.");
            }

            SaveMetadata metadata;

            try
            {
                metadata = MetadataCodec.Decode(
                    bytes, preamble.MetadataOffset, preamble.MetadataLength, includeThumbnail: false);
            }
            catch (EternalFormatException error)
            {
                return AdoptionReport.No(AdoptionVerdict.Malformed, error.Message);
            }

            // 3. Is it ours, whatever the folder it was sitting in is called?
            if (!identity.Accepts(metadata.ProductId))
            {
                return AdoptionReport.No(
                    AdoptionVerdict.ForeignProduct,
                    "The save belongs to product " + metadata.ProductId + " and we are " + identity.Current + ".",
                    metadata.ProductId, metadata.SchemaVersion, metadata.SavedAtUtc);
            }

            // 4. Can its data be brought forward to what this build expects?
            if (metadata.SchemaVersion > currentSchemaVersion)
            {
                return AdoptionReport.No(
                    AdoptionVerdict.SchemaTooNew,
                    "The save is schema " + metadata.SchemaVersion + " and this build writes " +
                    currentSchemaVersion + ".",
                    metadata.ProductId, metadata.SchemaVersion, metadata.SavedAtUtc);
            }

            if (hasMigrationPath != null && !hasMigrationPath(metadata.SchemaVersion))
            {
                return AdoptionReport.No(
                    AdoptionVerdict.NoMigrationPath,
                    "No migration leads from schema " + metadata.SchemaVersion + " to " +
                    currentSchemaVersion + ".",
                    metadata.ProductId, metadata.SchemaVersion, metadata.SavedAtUtc);
            }

            return AdoptionReport.Yes(metadata.ProductId, metadata.SchemaVersion, metadata.SavedAtUtc);
        }
    }
}
