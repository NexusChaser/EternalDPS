using Newtonsoft.Json;

namespace NexusChaser.EternalDPS.Serialization
{
    // Phase 0 SCAFFOLDING, but this one earns its keep: it deliberately touches a Newtonsoft
    // type. If the ETERNAL_NEWTONSOFT version define or the assembly reference were wrong, this
    // would fail to compile and we would find out now instead of in phase 2.
    // Removed when the real adapter lands.
    internal static class AssemblyScaffold
    {
        internal static string Probe()
        {
            return JsonConvert.SerializeObject(new { package = EternalPackage.PackageName });
        }
    }
}
