using UnityEditor;

namespace NexusChaser.EternalDPS.Unity.Editor
{
    // ANDAMIAJE de la fase 0. Se borra en la fase 7, cuando entren las ventanas.
    internal static class AssemblyScaffold
    {
        internal static bool IsCompiling => EditorApplication.isCompiling;
    }
}
