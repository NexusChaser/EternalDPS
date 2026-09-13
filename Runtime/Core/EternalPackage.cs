namespace NexusChaser.EternalDPS
{
    /// <summary>
    /// Datos del propio paquete. Vive en el nucleo a proposito: es el unico sitio al que
    /// llegan todos los demas ensamblados.
    /// </summary>
    public static class EternalPackage
    {
        /// <summary>Identificador UPM. Debe coincidir con el de package.json.</summary>
        public const string PackageName = "com.nexuschaser.eternaldps";

        /// <summary>
        /// Version del paquete. Se mantiene a mano en sincronia con package.json porque el
        /// nucleo no puede leerlo: eso exigiria UnityEditor, y este ensamblado no referencia
        /// el motor. Hay una prueba que comprueba que los dos numeros coinciden.
        /// </summary>
        public const string Version = "0.1.0";

        /// <summary>
        /// Extension de los archivos de guardado. Forma parte del contrato publico: una vez
        /// publicado un juego, no se cambia.
        /// </summary>
        public const string FileExtension = ".etm";
    }
}
