using System;
using System.Globalization;
using System.Text;

namespace NexusChaser.EternalDPS
{
    /// <summary>
    /// Addresses one record: <c>(Scope, Kind, RecordId, SlotId)</c>. This is what every call into
    /// the system takes, and what the relative path on disk is derived from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The slot is always present. A game with a single save passes nothing and gets
    /// <see cref="SlotId.Default"/>; an RPG passes a real one. There is one code path, not two.
    /// </para>
    /// <para>
    /// The layout has no segment for <see cref="RecordKind"/> — kind selects the policy, not the
    /// location. Two records in the same scope are told apart by their record id alone, so record
    /// ids must be unique within a scope even across kinds. <see cref="ConflictsWith"/> is how a
    /// test or a tool detects a violation.
    /// </para>
    /// </remarks>
    public readonly struct EternalKey : IEquatable<EternalKey>
    {
        /// <summary>Longest accepted record id. Well under every filesystem's component limit.</summary>
        public const int MaxRecordIdLength = 64;

        private readonly string _relativePath;

        /// <summary>
        /// Builds a key, validating the record id and collapsing the slot when the scope does not
        /// use one.
        /// </summary>
        /// <param name="scope">Who the record belongs to.</param>
        /// <param name="kind">What sort of record it is. Chooses the policy, not the location.</param>
        /// <param name="recordId">
        /// Lowercase identifier, unique within the scope. See <see cref="IsValidRecordId"/> for the
        /// accepted shape.
        /// </param>
        /// <param name="slot">
        /// The slot. Ignored — and forced back to <see cref="SlotId.Default"/> — unless
        /// <paramref name="scope"/> is <see cref="Scope.Slot"/>, so that two keys pointing at the
        /// same file always compare equal.
        /// </param>
        /// <exception cref="ArgumentException">The record id is not valid.</exception>
        public EternalKey(Scope scope, RecordKind kind, string recordId, SlotId slot = default)
        {
            if (!TryNormalizeRecordId(recordId, out var normalized, out var reason))
            {
                throw new ArgumentException(reason, nameof(recordId));
            }

            Scope = scope;
            Kind = kind;
            RecordId = normalized;
            Slot = scope == Scope.Slot ? slot : SlotId.Default;

            _relativePath = ComposePath(scope, normalized, Slot);
        }

        /// <summary>Who the record belongs to.</summary>
        public Scope Scope { get; }

        /// <summary>What sort of record it is.</summary>
        public RecordKind Kind { get; }

        /// <summary>The normalised, lowercase record id.</summary>
        public string RecordId { get; }

        /// <summary>
        /// The slot. Always <see cref="SlotId.Default"/> when <see cref="Scope"/> is not
        /// <see cref="Scope.Slot"/>.
        /// </summary>
        public SlotId Slot { get; }

        /// <summary>
        /// Where the record lives, relative to the store's root, using a forward slash as the
        /// separator on every platform. A store is free to map this onto whatever namespace it
        /// actually has — Steam Cloud, for one, has a flat filename space and will flatten it.
        /// </summary>
        /// <example>
        /// <code>
        /// machine/display.etm
        /// account/prefs.etm
        /// slots/default/save.etm
        /// slots/9f1cb2.../save.etm
        /// </code>
        /// </example>
        public string RelativePath => _relativePath ?? string.Empty;

        /// <summary>
        /// Shorthand for a machine-scoped setting, such as graphics quality or resolution.
        /// </summary>
        public static EternalKey MachineSettings(string recordId)
        {
            return new EternalKey(Scope.Machine, RecordKind.Settings, recordId);
        }

        /// <summary>Shorthand for an account-scoped preference, such as language or volume.</summary>
        public static EternalKey AccountSettings(string recordId)
        {
            return new EternalKey(Scope.Account, RecordKind.Settings, recordId);
        }

        /// <summary>Shorthand for account-wide progress that outlives every slot.</summary>
        public static EternalKey AccountProgress(string recordId)
        {
            return new EternalKey(Scope.Account, RecordKind.Progress, recordId);
        }

        /// <summary>Shorthand for progress belonging to one playthrough.</summary>
        public static EternalKey SlotProgress(string recordId, SlotId slot = default)
        {
            return new EternalKey(Scope.Slot, RecordKind.Progress, recordId, slot);
        }

        /// <summary>Shorthand for a resumable session in a slot.</summary>
        public static EternalKey SlotSession(string recordId, SlotId slot = default)
        {
            return new EternalKey(Scope.Slot, RecordKind.Session, recordId, slot);
        }

        /// <summary>
        /// Path of the Nth backup copy of this record. Generation 1 is the most recent one.
        /// </summary>
        /// <remarks>
        /// Generation 1 is plain <c>.bak</c> rather than <c>.bak1</c> so that the common case —
        /// one backup, which is what most games configure — produces the name everybody expects
        /// when they go looking for it by hand.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">Generation is below 1.</exception>
        public string RelativeBackupPath(int generation)
        {
            if (generation < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(generation), generation, "Backup generations start at 1.");
            }

            return generation == 1
                ? RelativePath + ".bak"
                : RelativePath + ".bak" + generation.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// True when two different keys would land on the same file. That can only happen when they
        /// agree on everything except <see cref="Kind"/>, which the layout does not encode.
        /// </summary>
        public bool ConflictsWith(EternalKey other)
        {
            return Kind != other.Kind
                   && string.Equals(RelativePath, other.RelativePath, StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether a record id is acceptable, without throwing. Use it to validate input before
        /// building a key.
        /// </summary>
        public static bool IsValidRecordId(string recordId)
        {
            return TryNormalizeRecordId(recordId, out _, out _);
        }

        /// <summary>
        /// Validates a record id and returns its canonical form, or the reason it was rejected.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Ids are lowercased. Windows filesystems are case-insensitive and Linux ones are not, so
        /// <c>MySave</c> and <c>mysave</c> would be one file on one platform and two on another.
        /// Folding the case makes them one file everywhere, which is the behaviour that cannot
        /// silently lose data.
        /// </para>
        /// <para>
        /// The accepted alphabet is deliberately narrow: ASCII letters, digits, <c>-</c> and
        /// <c>_</c>, starting with a letter or a digit. That rules out path traversal, directory
        /// separators, trailing dots and spaces, and Unicode normalisation differences between
        /// platforms — every one of which is a way for a record id to escape the store root or to
        /// name a different file than it did yesterday.
        /// </para>
        /// </remarks>
        public static bool TryNormalizeRecordId(string recordId, out string normalized, out string reason)
        {
            normalized = null;

            if (string.IsNullOrEmpty(recordId))
            {
                reason = "A record id cannot be null or empty.";
                return false;
            }

            if (recordId.Length > MaxRecordIdLength)
            {
                reason = "A record id cannot be longer than " + MaxRecordIdLength + " characters.";
                return false;
            }

            var builder = new StringBuilder(recordId.Length);

            for (var i = 0; i < recordId.Length; i++)
            {
                var c = recordId[i];
                var isUpper = c >= 'A' && c <= 'Z';
                var isLower = c >= 'a' && c <= 'z';
                var isDigit = c >= '0' && c <= '9';
                var isSeparator = c == '-' || c == '_';

                if (!isUpper && !isLower && !isDigit && !isSeparator)
                {
                    reason = "A record id may only contain ASCII letters, digits, '-' and '_'; found '" + c + "'.";
                    return false;
                }

                if (i == 0 && isSeparator)
                {
                    reason = "A record id must start with a letter or a digit.";
                    return false;
                }

                builder.Append(isUpper ? (char)(c + 32) : c);
            }

            var candidate = builder.ToString();

            if (IsReservedDeviceName(candidate))
            {
                reason = "'" + candidate + "' is a reserved device name on Windows and cannot be a file name.";
                return false;
            }

            normalized = candidate;
            reason = null;
            return true;
        }

        /// <summary>
        /// Names Windows still refuses to use as files, with or without an extension. Getting this
        /// wrong produces a save that works on every machine in the team except one.
        /// </summary>
        private static bool IsReservedDeviceName(string candidate)
        {
            switch (candidate)
            {
                case "con":
                case "prn":
                case "aux":
                case "nul":
                    return true;
            }

            // com1..com9 and lpt1..lpt9.
            if (candidate.Length != 4)
            {
                return false;
            }

            var digit = candidate[3];
            if (digit < '1' || digit > '9')
            {
                return false;
            }

            return candidate.StartsWith("com", StringComparison.Ordinal)
                   || candidate.StartsWith("lpt", StringComparison.Ordinal);
        }

        /// <summary>
        /// Joins the segments, leaving out the ones that do not apply. Machine and account records
        /// have no slot segment at all, rather than a placeholder one.
        /// </summary>
        private static string ComposePath(Scope scope, string recordId, SlotId slot)
        {
            var fileName = recordId + EternalPackage.FileExtension;

            switch (scope)
            {
                case Scope.Machine:
                    return "machine/" + fileName;

                case Scope.Account:
                    return "account/" + fileName;

                case Scope.Slot:
                    return "slots/" + slot + "/" + fileName;

                default:
                    throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown scope.");
            }
        }

        public override string ToString()
        {
            return Kind + ":" + RelativePath;
        }

        public bool Equals(EternalKey other)
        {
            return Scope == other.Scope
                   && Kind == other.Kind
                   && Slot == other.Slot
                   && string.Equals(RecordId, other.RecordId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is EternalKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Scope;
                hash = (hash * 397) ^ (int)Kind;
                hash = (hash * 397) ^ Slot.GetHashCode();
                hash = (hash * 397) ^ (RecordId != null ? RecordId.GetHashCode() : 0);
                return hash;
            }
        }

        public static bool operator ==(EternalKey left, EternalKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(EternalKey left, EternalKey right)
        {
            return !left.Equals(right);
        }
    }
}
