using UnityEngine;

namespace NexusChaser.EternalDPS.Unity
{
    // ANDAMIAJE de la fase 0. Toca UnityEngine a proposito: comprueba que ESTE ensamblado si
    // referencia el motor, al reves que Core y Tooling. Se borra en la fase 2.
    internal static class AssemblyScaffold
    {
        internal static string Describe()
        {
            return EternalPackage.PackageName + " sobre " + Application.platform;
        }
    }
}
