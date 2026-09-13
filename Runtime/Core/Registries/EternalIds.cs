namespace NexusChaser.EternalDPS
{
    /// <summary>
    /// The registered identifiers of the format.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A number is never reused.</strong> Not after a transform is removed, not after one
    /// turns out to be a bad idea. Every saved file that mentions a number is a claim about what
    /// that number means, and those files are on players' disks where nobody can go and edit them.
    /// Retire a number by leaving a gap.
    /// </para>
    /// <para>
    /// The low half belongs to the package and the high half to the game, so a game can add its own
    /// compression or its own format without ever colliding with something added here later.
    /// </para>
    /// <code>
    /// TRANSFORMS                     SERIALIZERS
    /// 0x01  Deflate                  0x01  JSON (Newtonsoft, UTF-8)
    /// 0x02  AES-256-CBC  (reserved)  0x02  Binary (MemoryPack)  (reserved)
    /// 0x03..0x7F  reserved, package  0x03..0x7F  reserved, package
    /// 0x80..0xFF  free for the game  0x80..0xFF  free for the game
    /// </code>
    /// </remarks>
    public static class EternalIds
    {
        /// <summary>Zero is not an identifier; it is what an uninitialised byte looks like.</summary>
        public const byte None = 0x00;

        /// <summary>First identifier the package reserves for itself.</summary>
        public const byte PackageRangeFirst = 0x01;

        /// <summary>Last identifier the package reserves for itself.</summary>
        public const byte PackageRangeLast = 0x7F;

        /// <summary>First identifier a game may use for its own implementations.</summary>
        public const byte GameRangeFirst = 0x80;

        /// <summary>Last identifier a game may use for its own implementations.</summary>
        public const byte GameRangeLast = 0xFF;

        /// <summary>Deflate compression.</summary>
        public const byte TransformDeflate = 0x01;

        /// <summary>AES-256-CBC. Reserved now, implemented in the encryption phase.</summary>
        public const byte TransformAes256Cbc = 0x02;

        /// <summary>JSON through Newtonsoft, encoded UTF-8.</summary>
        public const byte SerializerJson = 0x01;

        /// <summary>A binary format. Reserved so a build can adopt one without a format change.</summary>
        public const byte SerializerBinary = 0x02;

        /// <summary>True when the identifier belongs to the range a game may use.</summary>
        public static bool IsGameDefined(byte id)
        {
            return id >= GameRangeFirst;
        }

        /// <summary>True when the identifier belongs to the package's range.</summary>
        public static bool IsPackageDefined(byte id)
        {
            return id >= PackageRangeFirst && id <= PackageRangeLast;
        }
    }
}
