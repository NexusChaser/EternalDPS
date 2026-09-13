using NUnit.Framework;
using UnityEditor.PackageManager;

namespace NexusChaser.EternalDPS.Tests
{
    /// <summary>
    /// The package's first tests. They cover the only thing worth covering in phase 0: that the
    /// scaffolding resolves, and that the version written by hand in the core has not drifted away
    /// from package.json.
    /// </summary>
    public class PackageManifestTests
    {
        private static PackageInfo Package =>
            PackageInfo.FindForAssembly(typeof(EternalPackage).Assembly);

        [Test]
        public void Core_resolves_as_a_package()
        {
            Assert.IsNotNull(Package, "Eternal.Core does not belong to any resolved package.");
            Assert.AreEqual(EternalPackage.PackageName, Package.name);
        }

        [Test]
        public void Core_version_matches_package_json()
        {
            // These are kept in sync by hand because the core cannot read package.json: doing so
            // would require UnityEditor, and Eternal.Core deliberately has no engine references.
            // This test is what stops the two from drifting apart unnoticed.
            Assert.AreEqual(Package.version, EternalPackage.Version);
        }

        [Test]
        public void File_extension_is_the_one_in_the_contract()
        {
            // Part of the public file format. If anyone changes it, let it be deliberate.
            Assert.AreEqual(".etm", EternalPackage.FileExtension);
        }
    }
}
