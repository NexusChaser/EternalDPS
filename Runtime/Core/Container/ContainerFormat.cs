using System;

namespace NexusChaser.EternalDPS.Container
{
    /// <summary>What a container carries beyond its body.</summary>
    [Flags]
    public enum ContainerFlags : byte
    {
        /// <summary>Body only.</summary>
        None = 0,

        /// <summary>A metadata block sits between the preamble and the body.</summary>
        HasMetadata = 1 << 0,

        /// <summary>An HMAC-SHA256 signature closes the file.</summary>
        Signed = 1 << 1,
    }

    /// <summary>
    /// The constants of the <c>.etm</c> format. Once a game ships, these are a public contract:
    /// the format may be extended, never changed.
    /// </summary>
    /// <remarks>
    /// <code>
    /// PREAMBLE — plain, never transformed, never encrypted
    ///    0  4  magic "ETM1"
    ///    4  1  containerVersion
    ///    5  1  flags            bit0 metadata · bit1 signed
    ///    6  2  metaLen          length of the METADATA block
    ///    8  4  bodyLen          length of the BODY
    ///   12  1  serializerId
    ///   13  1  keyId            which key signed it
    ///   14  1  transformCount
    ///   15  n  transformIds[]   one byte each, IN ORDER
    /// METADATA — encoded, not encrypted, readable with a broken body
    /// BODY     — serialized, then every transform applied in order
    /// SIGNATURE — 32 bytes of HMAC-SHA256 over EVERYTHING above
    /// </code>
    /// </remarks>
    public static class ContainerFormat
    {
        /// <summary>The four bytes every container starts with.</summary>
        public static readonly byte[] Magic = { (byte)'E', (byte)'T', (byte)'M', (byte)'1' };

        /// <summary>Version of the envelope this build writes.</summary>
        public const byte CurrentVersion = 1;

        /// <summary>Highest envelope version this build can read.</summary>
        public const byte MaxSupportedVersion = 1;

        /// <summary>Bytes before the transform list.</summary>
        public const int PreambleFixedLength = 15;

        /// <summary>Length of the trailing HMAC-SHA256.</summary>
        public const int SignatureLength = 32;

        /// <summary>Offsets inside the preamble, for readers that walk it by hand.</summary>
        public const int OffsetMagic = 0;

        /// <inheritdoc cref="OffsetMagic"/>
        public const int OffsetVersion = 4;

        /// <inheritdoc cref="OffsetMagic"/>
        public const int OffsetFlags = 5;

        /// <inheritdoc cref="OffsetMagic"/>
        public const int OffsetMetaLength = 6;

        /// <inheritdoc cref="OffsetMagic"/>
        public const int OffsetBodyLength = 8;

        /// <inheritdoc cref="OffsetMagic"/>
        public const int OffsetSerializerId = 12;

        /// <summary>
        /// Where the key id lives. It has to be readable <em>before</em> anything is verified,
        /// because it is what says which key to verify with.
        /// </summary>
        public const int OffsetKeyId = 13;

        /// <inheritdoc cref="OffsetMagic"/>
        public const int OffsetTransformCount = 14;

        /// <inheritdoc cref="OffsetMagic"/>
        public const int OffsetTransformIds = 15;

        /// <summary>
        /// Largest metadata block the format can describe, because its length is a two byte field.
        /// A thumbnail is the only thing likely to approach it.
        /// </summary>
        public const int MaxMetadataLength = ushort.MaxValue;

        /// <summary>Largest number of transforms a single container can chain.</summary>
        public const int MaxTransformCount = byte.MaxValue;

        /// <summary>True when the first four bytes are ours.</summary>
        public static bool HasMagic(byte[] bytes)
        {
            if (bytes == null || bytes.Length < Magic.Length)
            {
                return false;
            }

            for (var i = 0; i < Magic.Length; i++)
            {
                if (bytes[i] != Magic[i])
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Thrown when bytes claiming to be a container are not shaped like one.
    /// </summary>
    /// <remarks>
    /// Callers inside the package catch this and turn it into a
    /// <see cref="LoadStatus.Corrupt"/> verdict. It escapes to a game only through the low-level
    /// container API, which a game is not expected to call directly.
    /// </remarks>
    public sealed class EternalFormatException : Exception
    {
        /// <summary>Creates the exception with a description of what did not add up.</summary>
        public EternalFormatException(string message) : base(message)
        {
        }

        /// <summary>Creates the exception from an underlying failure.</summary>
        public EternalFormatException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
