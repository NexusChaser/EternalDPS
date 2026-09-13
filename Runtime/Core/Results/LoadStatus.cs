namespace NexusChaser.EternalDPS
{
    /// <summary>
    /// Every way a load can end. A save that does not load is not an exceptional condition: it is an
    /// expected one that needs a product answer, which is why it is a value and not a thrown
    /// exception.
    /// </summary>
    /// <remarks>
    /// The verdict says what happened. It does not say what to do — that is the
    /// <see cref="SaveProfile"/>'s job. The same <see cref="IntegrityFailed"/> means "try the
    /// backup" for progress and "drop it and tell the player" for a session.
    /// </remarks>
    public enum LoadStatus
    {
        /// <summary>Loaded.</summary>
        Ok = 0,

        /// <summary>Nothing there. A new game, not a failure.</summary>
        NotFound = 1,

        /// <summary>
        /// The magic does not match. The file belongs to something else; leave it alone rather than
        /// claiming it.
        /// </summary>
        NotEternalFile = 2,

        /// <summary>
        /// The container version is higher than this build understands. Written by a newer version
        /// of the game — tell the player that, and do not overwrite it.
        /// </summary>
        ContainerTooNew = 3,

        /// <summary>
        /// The serialiser id in the preamble is not registered in this build. An optional module is
        /// missing, typically because the file was written by a build that had it compiled in.
        /// </summary>
        UnknownSerializer = 4,

        /// <summary>
        /// One of the transform ids is not registered in this build. Same cause as
        /// <see cref="UnknownSerializer"/>, different slot in the pipeline.
        /// </summary>
        UnknownTransform = 5,

        /// <summary>
        /// The signature does not verify. The bytes are not what was written. What happens next is
        /// the profile's decision.
        /// </summary>
        IntegrityFailed = 6,

        /// <summary>
        /// The schema version in the metadata is from the future. Reject it without touching the
        /// file: a downgrade that overwrites is how a player loses a save by opening the game on
        /// the wrong machine.
        /// </summary>
        SchemaTooNew = 7,

        /// <summary>The migration chain broke on the way to the current schema.</summary>
        MigrationFailed = 8,

        /// <summary>
        /// Parsing failed even though the signature was valid. The bytes are exactly what we wrote,
        /// and we still could not read them — so this is our bug, not the player's disk. It gets
        /// logged as one.
        /// </summary>
        Corrupt = 9,
    }
}
