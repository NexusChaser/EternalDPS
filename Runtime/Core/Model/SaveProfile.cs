using System;

namespace NexusChaser.EternalDPS
{
    /// <summary>How long a record is meant to survive.</summary>
    public enum RecordLifetime
    {
        /// <summary>Kept until the player or the game deletes it.</summary>
        Permanent = 0,

        /// <summary>
        /// Deleted once it has been consumed. A resumable match is the obvious case: it exists to
        /// be restored exactly once, and leaving it behind would let the player rewind.
        /// </summary>
        Session = 1,
    }

    /// <summary>Whether a record may leave the machine.</summary>
    public enum CloudPolicy
    {
        /// <summary>Eligible for cloud synchronisation.</summary>
        Sync = 0,

        /// <summary>
        /// Stays on this device. Machine settings are always local: pushing this computer's
        /// resolution onto the player's laptop is a bug, not a feature.
        /// </summary>
        Local = 1,
    }

    /// <summary>What to do when a record cannot be migrated to the current schema.</summary>
    public enum MigrationFailurePolicy
    {
        /// <summary>
        /// Report the failure and touch nothing. Correct for progress: the data may still be
        /// recoverable by a later build, and destroying it forecloses that.
        /// </summary>
        Fail = 0,

        /// <summary>
        /// Discard the record and tell the player. Correct for a session: losing a resumable match
        /// is a disappointment, not a disaster.
        /// </summary>
        DiscardAndNotify = 1,
    }

    /// <summary>What to do when the signature does not match.</summary>
    public enum IntegrityFailurePolicy
    {
        /// <summary>
        /// Fall back to the most recent backup, and say so. Never silently: if the player is sent
        /// back to an earlier point, they have to know it happened.
        /// </summary>
        TryBackup = 0,

        /// <summary>Discard the record and tell the player.</summary>
        DiscardAndNotify = 1,

        /// <summary>Report the failure and touch nothing.</summary>
        Fail = 2,
    }

    /// <summary>How to resolve two versions of the same record from different machines.</summary>
    public enum ConflictPolicy
    {
        /// <summary>Keep whichever was saved last.</summary>
        Newest = 0,

        /// <summary>Keep this machine's copy. Useful while debugging a sync problem.</summary>
        PreferLocal = 1,

        /// <summary>
        /// Combine them with game-specific logic — union of unlocks, maximum of counters. The
        /// resolver itself belongs to the synchronisation layer, which is not built yet; this value
        /// exists so the policy can be declared before that layer lands.
        /// </summary>
        Merge = 2,
    }

    /// <summary>
    /// The policy attached to a record: how long it lives, whether it syncs, and what happens when
    /// something goes wrong.
    /// </summary>
    /// <remarks>
    /// This is where "full save" and "progress only" stop being two systems and become two
    /// configurations of one. The verdict from a load says what happened; the profile says what to
    /// do about it.
    /// </remarks>
    public readonly struct SaveProfile : IEquatable<SaveProfile>
    {
        /// <summary>Builds a profile explicitly. Prefer the presets unless you need something else.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The backup count is negative.</exception>
        public SaveProfile(
            RecordLifetime lifetime,
            CloudPolicy cloud,
            MigrationFailurePolicy onMigrationFailure,
            IntegrityFailurePolicy onIntegrityFailure,
            ConflictPolicy conflict,
            int backups)
        {
            if (backups < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(backups), backups, "A profile cannot keep a negative number of backups.");
            }

            Lifetime = lifetime;
            Cloud = cloud;
            OnMigrationFailure = onMigrationFailure;
            OnIntegrityFailure = onIntegrityFailure;
            Conflict = conflict;
            Backups = backups;
        }

        /// <summary>Whether the record is deleted once consumed.</summary>
        public RecordLifetime Lifetime { get; }

        /// <summary>Whether the record may be synchronised off this device.</summary>
        public CloudPolicy Cloud { get; }

        /// <summary>Reaction to a broken migration chain.</summary>
        public MigrationFailurePolicy OnMigrationFailure { get; }

        /// <summary>Reaction to a failed signature check.</summary>
        public IntegrityFailurePolicy OnIntegrityFailure { get; }

        /// <summary>Reaction to the same record existing in two places with different contents.</summary>
        public ConflictPolicy Conflict { get; }

        /// <summary>
        /// How many previous versions to keep. Zero means none. For an RPG this number is the
        /// difference between "your forty hours are gone" and "we restored your previous save".
        /// </summary>
        public int Backups { get; }

        /// <summary>
        /// Graphics quality, resolution, audio device. Local by definition, and cheap enough to
        /// rebuild that a backup would be ceremony.
        /// </summary>
        public static SaveProfile MachineSettings => new SaveProfile(
            RecordLifetime.Permanent,
            CloudPolicy.Local,
            MigrationFailurePolicy.DiscardAndNotify,
            IntegrityFailurePolicy.DiscardAndNotify,
            ConflictPolicy.PreferLocal,
            backups: 0);

        /// <summary>Language, volume, accessibility. Follows the player between machines.</summary>
        public static SaveProfile AccountSettings => new SaveProfile(
            RecordLifetime.Permanent,
            CloudPolicy.Sync,
            MigrationFailurePolicy.DiscardAndNotify,
            IntegrityFailurePolicy.DiscardAndNotify,
            ConflictPolicy.Newest,
            backups: 1);

        /// <summary>
        /// Everything the player earned. Recover it by any means available; never throw it away on
        /// the system's own initiative.
        /// </summary>
        public static SaveProfile Progress => new SaveProfile(
            RecordLifetime.Permanent,
            CloudPolicy.Sync,
            MigrationFailurePolicy.Fail,
            IntegrityFailurePolicy.TryBackup,
            ConflictPolicy.Newest,
            backups: 2);

        /// <summary>
        /// A match in progress. If it cannot be restored, drop it and say so — do not crash, and do
        /// not resume something half-read.
        /// </summary>
        public static SaveProfile Session => new SaveProfile(
            RecordLifetime.Session,
            CloudPolicy.Sync,
            MigrationFailurePolicy.DiscardAndNotify,
            IntegrityFailurePolicy.DiscardAndNotify,
            ConflictPolicy.Newest,
            backups: 0);

        /// <summary>
        /// The preset that fits a scope and kind. A game is free to ignore this and declare its
        /// own; it exists so that the common case needs no decision.
        /// </summary>
        public static SaveProfile For(Scope scope, RecordKind kind)
        {
            switch (kind)
            {
                case RecordKind.Settings:
                    return scope == Scope.Machine ? MachineSettings : AccountSettings;

                case RecordKind.Progress:
                    return Progress;

                case RecordKind.Session:
                    return Session;

                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown record kind.");
            }
        }

        /// <summary>The same profile with a different number of backups.</summary>
        public SaveProfile WithBackups(int backups)
        {
            return new SaveProfile(Lifetime, Cloud, OnMigrationFailure, OnIntegrityFailure, Conflict, backups);
        }

        /// <summary>The same profile with a different cloud policy.</summary>
        public SaveProfile WithCloud(CloudPolicy cloud)
        {
            return new SaveProfile(Lifetime, cloud, OnMigrationFailure, OnIntegrityFailure, Conflict, Backups);
        }

        /// <summary>The same profile with a different conflict policy.</summary>
        public SaveProfile WithConflict(ConflictPolicy conflict)
        {
            return new SaveProfile(Lifetime, Cloud, OnMigrationFailure, OnIntegrityFailure, conflict, Backups);
        }

        /// <summary>The same profile with a different reaction to a failed signature check.</summary>
        public SaveProfile WithIntegrityFailure(IntegrityFailurePolicy policy)
        {
            return new SaveProfile(Lifetime, Cloud, OnMigrationFailure, policy, Conflict, Backups);
        }

        /// <summary>The same profile with a different reaction to a broken migration.</summary>
        public SaveProfile WithMigrationFailure(MigrationFailurePolicy policy)
        {
            return new SaveProfile(Lifetime, Cloud, policy, OnIntegrityFailure, Conflict, Backups);
        }

        public bool Equals(SaveProfile other)
        {
            return Lifetime == other.Lifetime
                   && Cloud == other.Cloud
                   && OnMigrationFailure == other.OnMigrationFailure
                   && OnIntegrityFailure == other.OnIntegrityFailure
                   && Conflict == other.Conflict
                   && Backups == other.Backups;
        }

        public override bool Equals(object obj)
        {
            return obj is SaveProfile other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Lifetime;
                hash = (hash * 397) ^ (int)Cloud;
                hash = (hash * 397) ^ (int)OnMigrationFailure;
                hash = (hash * 397) ^ (int)OnIntegrityFailure;
                hash = (hash * 397) ^ (int)Conflict;
                hash = (hash * 397) ^ Backups;
                return hash;
            }
        }

        public static bool operator ==(SaveProfile left, SaveProfile right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(SaveProfile left, SaveProfile right)
        {
            return !left.Equals(right);
        }
    }
}
