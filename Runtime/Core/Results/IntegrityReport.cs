namespace NexusChaser.EternalDPS
{
    /// <summary>The four regions of an <c>.etm</c> container, in the order they appear.</summary>
    public enum ContainerRegion
    {
        /// <summary>No region in particular, or the failure happened before any could be read.</summary>
        None = 0,

        /// <summary>Fixed header: magic, version, flags, lengths, serialiser, key id, transforms.</summary>
        Preamble = 1,

        /// <summary>Product id, schema version, timestamps, slot name, playtime, thumbnail.</summary>
        Metadata = 2,

        /// <summary>The serialised payload with its transform chain applied.</summary>
        Body = 3,

        /// <summary>The trailing HMAC-SHA256 over everything above it.</summary>
        Signature = 4,
    }

    /// <summary>Why a container did or did not verify.</summary>
    public enum IntegrityStatus
    {
        /// <summary>Everything checked out.</summary>
        Valid = 0,

        /// <summary>The first four bytes are not the magic. Almost certainly not one of our files.</summary>
        MagicMismatch = 1,

        /// <summary>The file ends before the header says it should.</summary>
        Truncated = 2,

        /// <summary>
        /// The declared lengths do not add up to the file's real length. Something appended to it,
        /// or a write was interrupted partway.
        /// </summary>
        LengthMismatch = 3,

        /// <summary>
        /// The recomputed HMAC does not match the stored one. The file was modified after it was
        /// written — by an editor, by a corrupted disk, or by a player with a hex editor.
        /// </summary>
        SignatureMismatch = 4,

        /// <summary>
        /// The file carries no signature. Only produced by a build that deliberately wrote one
        /// unsigned, which is a development convenience and never ships.
        /// </summary>
        Unsigned = 5,

        /// <summary>
        /// The file names a key this build does not have. Either it came from a newer version, or
        /// the key was retired and dropped from the set.
        /// </summary>
        UnknownKey = 6,
    }

    /// <summary>
    /// What a verification pass found: the verdict, which region it broke in, and the sizes it read
    /// along the way.
    /// </summary>
    /// <remarks>
    /// The sizes matter as much as the verdict. "The signature does not match" and "the file is
    /// 40 bytes shorter than its header claims" point at different causes — tampering versus an
    /// interrupted write — and lead to different fixes. A report that only says "invalid" makes
    /// that distinction impossible after the fact.
    /// </remarks>
    public sealed class IntegrityReport
    {
        private IntegrityReport(
            IntegrityStatus status,
            ContainerRegion failedRegion,
            string detail,
            byte containerVersion,
            byte keyId,
            int declaredMetadataLength,
            int declaredBodyLength,
            long actualLength)
        {
            Status = status;
            FailedRegion = failedRegion;
            Detail = detail;
            ContainerVersion = containerVersion;
            KeyId = keyId;
            DeclaredMetadataLength = declaredMetadataLength;
            DeclaredBodyLength = declaredBodyLength;
            ActualLength = actualLength;
        }

        /// <summary>The verdict.</summary>
        public IntegrityStatus Status { get; }

        /// <summary>Where it went wrong. <see cref="ContainerRegion.None"/> when it did not.</summary>
        public ContainerRegion FailedRegion { get; }

        /// <summary>
        /// A sentence for a log or a tool. Never shown to a player, and never the thing code
        /// branches on — that is what <see cref="Status"/> is for.
        /// </summary>
        public string Detail { get; }

        /// <summary>Container version read from the preamble, or zero if it was not reached.</summary>
        public byte ContainerVersion { get; }

        /// <summary>Key id read from the preamble, or zero if it was not reached.</summary>
        public byte KeyId { get; }

        /// <summary>Metadata length the preamble declared.</summary>
        public int DeclaredMetadataLength { get; }

        /// <summary>Body length the preamble declared.</summary>
        public int DeclaredBodyLength { get; }

        /// <summary>Bytes actually available. Compare against the declared lengths.</summary>
        public long ActualLength { get; }

        /// <summary>True only for <see cref="IntegrityStatus.Valid"/>.</summary>
        public bool IsValid => Status == IntegrityStatus.Valid;

        /// <summary>A passing report.</summary>
        public static IntegrityReport Valid(
            byte containerVersion,
            byte keyId,
            int declaredMetadataLength,
            int declaredBodyLength,
            long actualLength)
        {
            return new IntegrityReport(
                IntegrityStatus.Valid,
                ContainerRegion.None,
                null,
                containerVersion,
                keyId,
                declaredMetadataLength,
                declaredBodyLength,
                actualLength);
        }

        /// <summary>A failing report, with whatever was read before the failure.</summary>
        public static IntegrityReport Failed(
            IntegrityStatus status,
            ContainerRegion region,
            string detail,
            byte containerVersion = 0,
            byte keyId = 0,
            int declaredMetadataLength = 0,
            int declaredBodyLength = 0,
            long actualLength = 0)
        {
            return new IntegrityReport(
                status,
                region,
                detail,
                containerVersion,
                keyId,
                declaredMetadataLength,
                declaredBodyLength,
                actualLength);
        }

        /// <summary>
        /// A failing report for a file whose first bytes are not ours. Kept separate because it is
        /// the one failure that is usually not a problem at all — it just means the file belongs to
        /// someone else.
        /// </summary>
        public static IntegrityReport NotEternalFile(long actualLength)
        {
            return Failed(
                IntegrityStatus.MagicMismatch,
                ContainerRegion.Preamble,
                "The file does not start with the Eternal container magic.",
                actualLength: actualLength);
        }

        public override string ToString()
        {
            if (IsValid)
            {
                return "Integrity: Valid (metadata " + DeclaredMetadataLength +
                       " B, body " + DeclaredBodyLength + " B, file " + ActualLength + " B)";
            }

            return "Integrity: " + Status + " in " + FailedRegion +
                   (string.IsNullOrEmpty(Detail) ? string.Empty : " — " + Detail);
        }
    }
}
