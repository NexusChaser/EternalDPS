using System;

namespace NexusChaser.EternalDPS.Container
{
    /// <summary>
    /// The header of a container, parsed. Everything needed to find the other regions and to know
    /// what has to be undone to read them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Parsing this applies <strong>no transform and verifies nothing</strong>, by necessity: the
    /// preamble is what says which transforms were used and which key signed the file, so it has to
    /// be readable before any of that is known. It is therefore the one region that is never
    /// compressed and never encrypted.
    /// </para>
    /// <para>
    /// It is still covered by the signature. Otherwise stripping the encryption id from the
    /// transform list would be enough to make a reader hand over a payload it should have refused —
    /// the file would simply claim it was never encrypted.
    /// </para>
    /// </remarks>
    public readonly struct ContainerPreamble
    {
        internal ContainerPreamble(
            byte version,
            ContainerFlags flags,
            int metadataLength,
            int bodyLength,
            byte serializerId,
            byte keyId,
            byte[] transformIds)
        {
            Version = version;
            Flags = flags;
            MetadataLength = metadataLength;
            BodyLength = bodyLength;
            SerializerId = serializerId;
            KeyId = keyId;
            TransformIds = transformIds ?? new byte[0];
        }

        /// <summary>Version of the envelope.</summary>
        public byte Version { get; }

        /// <summary>What the container carries beyond the body.</summary>
        public ContainerFlags Flags { get; }

        /// <summary>Length of the metadata block. Zero when there is none.</summary>
        public int MetadataLength { get; }

        /// <summary>Length of the body, with its transforms already applied.</summary>
        public int BodyLength { get; }

        /// <summary>Which serialiser produced the body.</summary>
        public byte SerializerId { get; }

        /// <summary>Which key signed the file. Meaningless when it is not signed.</summary>
        public byte KeyId { get; }

        /// <summary>
        /// The transforms applied to the body, <strong>in the order they were applied</strong>.
        /// Reading undoes them back to front.
        /// </summary>
        public byte[] TransformIds { get; }

        /// <summary>True when a signature closes the file.</summary>
        public bool IsSigned => (Flags & ContainerFlags.Signed) != 0;

        /// <summary>True when a metadata block sits between the preamble and the body.</summary>
        public bool HasMetadata => (Flags & ContainerFlags.HasMetadata) != 0;

        /// <summary>Total length of the preamble, including the transform list.</summary>
        public int Length => ContainerFormat.PreambleFixedLength + TransformIds.Length;

        /// <summary>Offset of the metadata block.</summary>
        public int MetadataOffset => Length;

        /// <summary>Offset of the body.</summary>
        public int BodyOffset => MetadataOffset + MetadataLength;

        /// <summary>Offset of the signature. Past the end of the file when unsigned.</summary>
        public int SignatureOffset => BodyOffset + BodyLength;

        /// <summary>How long the whole file should be if the header is telling the truth.</summary>
        public int ExpectedTotalLength => SignatureOffset + (IsSigned ? ContainerFormat.SignatureLength : 0);

        /// <summary>Writes this preamble into a buffer at offset zero.</summary>
        internal void WriteTo(byte[] buffer)
        {
            Array.Copy(ContainerFormat.Magic, 0, buffer, ContainerFormat.OffsetMagic, ContainerFormat.Magic.Length);

            buffer[ContainerFormat.OffsetVersion] = Version;
            buffer[ContainerFormat.OffsetFlags] = (byte)Flags;
            Bytes.WriteUInt16(buffer, ContainerFormat.OffsetMetaLength, (ushort)MetadataLength);
            Bytes.WriteUInt32(buffer, ContainerFormat.OffsetBodyLength, (uint)BodyLength);
            buffer[ContainerFormat.OffsetSerializerId] = SerializerId;
            buffer[ContainerFormat.OffsetKeyId] = KeyId;
            buffer[ContainerFormat.OffsetTransformCount] = (byte)TransformIds.Length;

            Array.Copy(TransformIds, 0, buffer, ContainerFormat.OffsetTransformIds, TransformIds.Length);
        }

        /// <summary>
        /// Parses a preamble, checking every claim it makes against the bytes that are actually
        /// there.
        /// </summary>
        /// <remarks>
        /// Failures come back as an <see cref="IntegrityReport"/> rather than an exception, because
        /// "this file is not ours" and "this file was cut short" are both ordinary things to run
        /// into when scanning a folder.
        /// </remarks>
        public static bool TryParse(byte[] bytes, out ContainerPreamble preamble, out IntegrityReport failure)
        {
            preamble = default;
            failure = null;

            var available = bytes?.Length ?? 0;

            if (!ContainerFormat.HasMagic(bytes))
            {
                failure = IntegrityReport.NotEternalFile(available);
                return false;
            }

            if (available < ContainerFormat.PreambleFixedLength)
            {
                failure = IntegrityReport.Failed(
                    IntegrityStatus.Truncated,
                    ContainerRegion.Preamble,
                    "The file is shorter than a preamble.",
                    actualLength: available);
                return false;
            }

            var version = bytes[ContainerFormat.OffsetVersion];

            // Checked before anything else is read, because a newer envelope may not put the
            // remaining fields where this build expects them. Nothing past here would be trustworthy.
            if (version > ContainerFormat.MaxSupportedVersion)
            {
                failure = IntegrityReport.Failed(
                    IntegrityStatus.UnsupportedVersion,
                    ContainerRegion.Preamble,
                    "The container is version " + version + " and this build reads up to " +
                    ContainerFormat.MaxSupportedVersion + ". It was written by a newer build.",
                    version,
                    actualLength: available);
                return false;
            }

            var flags = (ContainerFlags)bytes[ContainerFormat.OffsetFlags];
            var metadataLength = Bytes.ReadUInt16(bytes, ContainerFormat.OffsetMetaLength);
            var bodyLengthRaw = Bytes.ReadUInt32(bytes, ContainerFormat.OffsetBodyLength);
            var serializerId = bytes[ContainerFormat.OffsetSerializerId];
            var keyId = bytes[ContainerFormat.OffsetKeyId];
            var transformCount = bytes[ContainerFormat.OffsetTransformCount];

            // A body length from a corrupted header can be anything up to 4 GB. Reject it against
            // the bytes actually present instead of trying to allocate it.
            if (bodyLengthRaw > int.MaxValue)
            {
                failure = IntegrityReport.Failed(
                    IntegrityStatus.LengthMismatch,
                    ContainerRegion.Preamble,
                    "The declared body length does not fit in this runtime.",
                    version, keyId, metadataLength, 0, available);
                return false;
            }

            var bodyLength = (int)bodyLengthRaw;
            var preambleLength = ContainerFormat.PreambleFixedLength + transformCount;

            if (available < preambleLength)
            {
                failure = IntegrityReport.Failed(
                    IntegrityStatus.Truncated,
                    ContainerRegion.Preamble,
                    "The file ends inside the transform list.",
                    version, keyId, metadataLength, bodyLength, available);
                return false;
            }

            var transformIds = new byte[transformCount];
            Array.Copy(bytes, ContainerFormat.OffsetTransformIds, transformIds, 0, transformCount);

            var candidate = new ContainerPreamble(
                version, flags, metadataLength, bodyLength, serializerId, keyId, transformIds);

            // Metadata length is only meaningful when the flag says there is metadata; a file that
            // declares a length without the flag, or the other way round, is malformed.
            if (candidate.HasMetadata == (metadataLength == 0))
            {
                failure = IntegrityReport.Failed(
                    IntegrityStatus.LengthMismatch,
                    ContainerRegion.Preamble,
                    candidate.HasMetadata
                        ? "The metadata flag is set but the metadata block is empty."
                        : "A metadata block is declared but the metadata flag is not set.",
                    version, keyId, metadataLength, bodyLength, available);
                return false;
            }

            if (available != candidate.ExpectedTotalLength)
            {
                var truncated = available < candidate.ExpectedTotalLength;

                failure = IntegrityReport.Failed(
                    truncated ? IntegrityStatus.Truncated : IntegrityStatus.LengthMismatch,
                    truncated ? ContainerRegion.Body : ContainerRegion.Preamble,
                    "The header describes a file of " + candidate.ExpectedTotalLength +
                    " bytes and there are " + available + ".",
                    version, keyId, metadataLength, bodyLength, available);
                return false;
            }

            preamble = candidate;
            return true;
        }
    }
}
