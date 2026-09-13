using System;

namespace NexusChaser.EternalDPS
{
    /// <summary>
    /// Identifies a save slot. Opaque and stable for the life of the slot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The identifier is a GUID and <strong>never the slot's position in a list</strong>. If slots
    /// were numbered 1..6 by display order, deleting slot 2 would renumber every slot after it,
    /// which silently repoints cloud files and any reference the game kept. The same lesson applies
    /// to every serialised enum: a stored value must not depend on ordering.
    /// </para>
    /// <para>
    /// The slot's name and its position on screen are <em>separate data</em>, stored in the record's
    /// metadata. Renaming a slot must never touch its identity.
    /// </para>
    /// <para>
    /// There is deliberately no ordering comparison on this type. Sorting by GUID would produce a
    /// stable but meaningless order, and offering it would invite exactly the mistake this type
    /// exists to prevent.
    /// </para>
    /// </remarks>
    public readonly struct SlotId : IEquatable<SlotId>
    {
        /// <summary>
        /// Path segment used by <see cref="Default"/>. A real slot's segment is 32 hex characters,
        /// so this word can never collide with one.
        /// </summary>
        private const string DefaultSegment = "default";

        private readonly Guid _value;

        private SlotId(Guid value)
        {
            _value = value;
        }

        /// <summary>
        /// The single implicit slot, for games that never show a slot picker. It is what a caller
        /// gets when it does not ask for a slot, so those games run the same code path as an RPG
        /// with six of them — no <c>if (usesSlots)</c> branch anywhere.
        /// </summary>
        public static SlotId Default => default;

        /// <summary>True for <see cref="Default"/> and nothing else.</summary>
        public bool IsDefault => _value == Guid.Empty;

        /// <summary>The underlying GUID. <see cref="Guid.Empty"/> for <see cref="Default"/>.</summary>
        public Guid Value => _value;

        /// <summary>Mints an identifier for a brand new slot.</summary>
        public static SlotId New()
        {
            return new SlotId(Guid.NewGuid());
        }

        /// <summary>Rebuilds an identifier from a GUID that was stored earlier.</summary>
        public static SlotId FromGuid(Guid value)
        {
            return new SlotId(value);
        }

        /// <summary>
        /// Reads back what <see cref="ToString"/> produced. Accepts both the default segment and
        /// any format <see cref="Guid.TryParse"/> understands, so files written by hand still load.
        /// </summary>
        public static bool TryParse(string text, out SlotId slot)
        {
            slot = Default;

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            if (string.Equals(text, DefaultSegment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!Guid.TryParse(text, out var guid))
            {
                return false;
            }

            slot = new SlotId(guid);
            return true;
        }

        /// <summary>
        /// The path segment for this slot: <c>default</c>, or 32 lowercase hex characters with no
        /// separators. Lowercase hex means the segment is identical on a case-sensitive filesystem
        /// and a case-insensitive one.
        /// </summary>
        public override string ToString()
        {
            return IsDefault ? DefaultSegment : _value.ToString("N");
        }

        public bool Equals(SlotId other)
        {
            return _value.Equals(other._value);
        }

        public override bool Equals(object obj)
        {
            return obj is SlotId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _value.GetHashCode();
        }

        public static bool operator ==(SlotId left, SlotId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(SlotId left, SlotId right)
        {
            return !left.Equals(right);
        }
    }
}
