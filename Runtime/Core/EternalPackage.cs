namespace NexusChaser.EternalDPS
{
    /// <summary>
    /// Facts about the package itself. It lives in the core on purpose: this is the one assembly
    /// every other one can reach.
    /// </summary>
    public static class EternalPackage
    {
        /// <summary>UPM identifier. Must match the one in package.json.</summary>
        public const string PackageName = "com.nexuschaser.eternaldps";

        /// <summary>
        /// Package version, kept in sync with package.json by hand. The core cannot read the
        /// manifest itself: that would require UnityEditor, and this assembly deliberately has no
        /// engine references. A test asserts that the two numbers still match.
        /// </summary>
        public const string Version = "0.1.0";

        /// <summary>
        /// Extension used by save files. Part of the public contract: once a game ships, it does
        /// not change.
        /// </summary>
        public const string FileExtension = ".etm";
    }
}
