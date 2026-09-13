using System.IO;
using NexusChaser.EternalDPS.Stores;
using UnityEngine;

namespace NexusChaser.EternalDPS.Unity
{
    /// <summary>
    /// Where saves live on this platform.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the <em>only</em> thing the engine layer contributes to storage. The store itself
    /// needs nothing but <see cref="System.IO"/> and lives in the core, which is what lets the
    /// command line tools and the test suite use it without opening Unity.
    /// </para>
    /// <para>
    /// The layout under the root — <c>machine/</c>, <c>account/</c>, <c>slots/</c> — is not built
    /// here. It falls out of <see cref="EternalKey"/>, so the same key names the same record on
    /// every platform and inside Steam Cloud, where there are no folders at all.
    /// </para>
    /// </remarks>
    public static class EternalPaths
    {
        /// <summary>
        /// The folder saves go in: <c>Application.persistentDataPath</c>.
        /// </summary>
        /// <remarks>
        /// Built by Unity from the company and product names, which is exactly why the identity of a
        /// save lives inside the file instead of in this path — renaming either of those sends this
        /// somewhere new and empty. See <see cref="ProductIdentity"/>.
        /// </remarks>
        public static string DataRoot => Application.persistentDataPath;

        /// <summary>
        /// True on platforms where a plain file store is not enough.
        /// </summary>
        /// <remarks>
        /// On WebGL <c>persistentDataPath</c> is a virtual filesystem backed by IndexedDB, and Unity
        /// does not flush it to the browser on its own. A file store there appears to work and loses
        /// everything the moment the tab closes, which is the worst possible failure: silent, and
        /// only in production.
        /// </remarks>
        public static bool NeedsPlatformStore =>
            Application.platform == RuntimePlatform.WebGLPlayer;

        /// <summary>
        /// Creates the file store for this machine.
        /// </summary>
        /// <param name="subFolder">
        /// Optional folder under the root, for a game that keeps other things in
        /// <c>persistentDataPath</c> and wants its saves apart from them.
        /// </param>
        public static FileStore CreateFileStore(string subFolder = null)
        {
            var root = string.IsNullOrEmpty(subFolder)
                ? DataRoot
                : Path.Combine(DataRoot, subFolder);

            return new FileStore(root);
        }
    }
}
