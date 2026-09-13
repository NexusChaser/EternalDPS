namespace NexusChaser.EternalDPS.Tooling
{
    // ANDAMIAJE de la fase 0. Solo existe para que el ensamblado se compile de verdad y el
    // cableado quede verificado en vez de supuesto. Se borra en la fase 6, cuando entre
    // EternalDocument.
    internal static class AssemblyScaffold
    {
        internal const string BelongsTo = EternalPackage.PackageName;
    }
}
