namespace NexusChaser.EternalDPS
{
    /// <summary>
    /// What kind of record this is. The second axis, independent from <see cref="Scope"/>.
    /// </summary>
    /// <remarks>
    /// Kind decides <em>policy</em>, not location: it is what a <see cref="SaveProfile"/> is chosen
    /// from. Two records of different kinds can sit side by side in the same scope folder, so the
    /// record id has to be unique within a scope regardless of kind. See
    /// <see cref="EternalKey.ConflictsWith"/>.
    /// </remarks>
    public enum RecordKind
    {
        /// <summary>Preferences. Cheap to lose, annoying to lose, never worth failing a boot over.</summary>
        Settings = 0,

        /// <summary>
        /// Everything the player earned. If it fails to load, the correct reaction is to try
        /// every recovery avenue available before giving up.
        /// </summary>
        Progress = 1,

        /// <summary>
        /// A resumable moment: exact position, board state, whose turn it is. If it fails to load,
        /// the correct reaction is to discard it and say so, not to crash and not to guess.
        /// </summary>
        Session = 2,
    }
}
