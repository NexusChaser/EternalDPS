namespace NexusChaser.EternalDPS
{
    /// <summary>
    /// The combinations of <see cref="Scope"/> and <see cref="RecordKind"/> that mean something.
    /// </summary>
    /// <remarks>
    /// This is <strong>advisory and not enforced</strong>. <see cref="EternalKey"/> accepts any
    /// combination, because a game may have a reason the matrix did not anticipate and a save
    /// system is a bad place to be clever at the developer's expense. Tooling and tests use this to
    /// flag the combinations that are almost always a mistake — machine-scoped progress, for
    /// instance, which is progress that evaporates when the player changes computer.
    /// </remarks>
    public static class ScopeRules
    {
        /// <summary>
        /// True when the pairing is one of the six the design accounts for.
        /// </summary>
        public static bool IsMeaningful(Scope scope, RecordKind kind)
        {
            switch (scope)
            {
                // Settings about the hardware. Progress or a session here would be lost on the
                // next machine, which is never what anyone wants.
                case Scope.Machine:
                    return kind == RecordKind.Settings;

                // Preferences and everything the player owns regardless of playthrough.
                // A session is a playthrough by definition, so it does not belong here.
                case Scope.Account:
                    return kind == RecordKind.Settings || kind == RecordKind.Progress;

                // A playthrough. Settings are not per-playthrough.
                case Scope.Slot:
                    return kind == RecordKind.Progress || kind == RecordKind.Session;

                default:
                    return false;
            }
        }
    }
}
