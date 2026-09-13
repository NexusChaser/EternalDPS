using UnityEngine;

namespace NexusChaser.EternalDPS.Unity
{
    // Phase 0 SCAFFOLDING. It touches UnityEngine on purpose: this proves THIS assembly does
    // reference the engine, unlike Core and Tooling. Removed in phase 2.
    internal static class AssemblyScaffold
    {
        internal static string Describe()
        {
            return EternalPackage.PackageName + " on " + Application.platform;
        }
    }
}
