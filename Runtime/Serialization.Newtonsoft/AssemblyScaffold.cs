using Newtonsoft.Json;

namespace NexusChaser.EternalDPS.Serialization
{
    // ANDAMIAJE de la fase 0, pero este gana algo por su cuenta: toca un tipo de Newtonsoft a
    // proposito. Si el version define ETERNAL_NEWTONSOFT o la referencia al ensamblado
    // estuvieran mal puestos, esto no compilaria y nos enterariamos ahora y no en la fase 2.
    // Se borra cuando entre el adaptador de verdad (SER-01).
    internal static class AssemblyScaffold
    {
        internal static string Probe()
        {
            return JsonConvert.SerializeObject(new { package = EternalPackage.PackageName });
        }
    }
}
