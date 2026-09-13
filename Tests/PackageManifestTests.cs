using NUnit.Framework;
using UnityEditor.PackageManager;

namespace NexusChaser.EternalDPS.Tests
{
    /// <summary>
    /// Primera prueba del paquete. Comprueba lo unico que hay que comprobar en la fase 0: que el
    /// andamiaje esta bien montado y que la version escrita a mano en el nucleo no se ha quedado
    /// atras respecto a package.json.
    /// </summary>
    public class PackageManifestTests
    {
        private static PackageInfo Package =>
            PackageInfo.FindForAssembly(typeof(EternalPackage).Assembly);

        [Test]
        public void El_nucleo_se_resuelve_como_paquete()
        {
            Assert.IsNotNull(Package, "Eternal.Core no pertenece a ningun paquete resuelto.");
            Assert.AreEqual(EternalPackage.PackageName, Package.name);
        }

        [Test]
        public void La_version_del_nucleo_coincide_con_package_json()
        {
            // Se mantienen a mano porque el nucleo no puede leer package.json: hacerlo exigiria
            // UnityEditor, y Eternal.Core no referencia el motor a proposito. Esta prueba es lo
            // que evita que se separen sin que nadie se entere.
            Assert.AreEqual(Package.version, EternalPackage.Version);
        }

        [Test]
        public void La_extension_de_archivo_es_la_del_contrato()
        {
            // Forma parte del formato publico. Si alguien la cambia, que sea a sabiendas.
            Assert.AreEqual(".etm", EternalPackage.FileExtension);
        }
    }
}
