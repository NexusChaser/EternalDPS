using System;
using System.Collections.Generic;

namespace NexusChaser.EternalDPS
{
    /// <summary>
    /// What a save says about itself: who wrote it, when, and enough to draw it in a slot list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This block lives <strong>outside the body and is never encrypted</strong>, for two reasons.
    /// A slot picker has to draw six entries reading six headers, not deserialising six complete
    /// playthroughs. And — the part almost nobody implements — it has to still be readable when the
    /// body is corrupt, so that a damaged save does not vanish from the list and leave the player
    /// unable to even delete it.
    /// </para>
    /// <para>
    /// Leaving it in the clear is a deliberate concession: a level number and a playtime are not
    /// secrets. Anything that would actually hurt if read belongs in the body.
    /// </para>
    /// </remarks>
    public sealed class SaveMetadata
    {
        private Dictionary<string, string> _custom;

        /// <summary>
        /// Which game wrote this, as a GUID frozen for the life of the product.
        /// </summary>
        /// <remarks>
        /// Not the product's name, which gets renamed — reliably at the worst moment — and takes
        /// the save folder with it. The identity lives inside the file so that renaming stops being
        /// an event. See <see cref="ProductIdentity"/>.
        /// </remarks>
        public Guid ProductId { get; set; }

        /// <summary>
        /// Shape of the game's own data, for migrations. Independent of the container version,
        /// which describes the envelope; confusing the two is a classic mistake.
        /// </summary>
        public uint SchemaVersion { get; set; }

        /// <summary>When it was written. Decides which of two copies is newer.</summary>
        public DateTimeOffset SavedAtUtc { get; set; }

        /// <summary>Build that wrote it. For support, and for reading a bug report.</summary>
        public string AppVersion { get; set; }

        /// <summary>
        /// What the player calls this save. Free text: unlike the record id, it has no character
        /// restrictions, because it never becomes a file name.
        /// </summary>
        public string SlotName { get; set; }

        /// <summary>Time played, for the slot list.</summary>
        public TimeSpan Playtime { get; set; }

        /// <summary>
        /// Optional preview image, stored as already-encoded bytes — PNG or JPEG, whatever the game
        /// produced. The container does not look inside it.
        /// </summary>
        public byte[] Thumbnail { get; set; }

        /// <summary>
        /// Extra text the game wants visible without opening the body: a chapter name, a
        /// difficulty, the number of players. Anything structured belongs in the body instead.
        /// </summary>
        public IDictionary<string, string> Custom => _custom ?? (_custom = new Dictionary<string, string>(StringComparer.Ordinal));

        /// <summary>True when <see cref="Custom"/> holds anything, without allocating it.</summary>
        public bool HasCustomFields => _custom != null && _custom.Count > 0;

        /// <summary>True when a preview image is present.</summary>
        public bool HasThumbnail => Thumbnail != null && Thumbnail.Length > 0;

        /// <summary>A copy, so that handing metadata to a caller cannot let it edit ours.</summary>
        public SaveMetadata Clone()
        {
            var copy = new SaveMetadata
            {
                ProductId = ProductId,
                SchemaVersion = SchemaVersion,
                SavedAtUtc = SavedAtUtc,
                AppVersion = AppVersion,
                SlotName = SlotName,
                Playtime = Playtime,
                Thumbnail = Thumbnail == null ? null : (byte[])Thumbnail.Clone(),
            };

            if (HasCustomFields)
            {
                foreach (var pair in _custom)
                {
                    copy.Custom[pair.Key] = pair.Value;
                }
            }

            return copy;
        }

        public override string ToString()
        {
            return (string.IsNullOrEmpty(SlotName) ? "(unnamed)" : SlotName) +
                   " · schema " + SchemaVersion +
                   " · " + SavedAtUtc.UtcDateTime.ToString("u") +
                   " · " + Playtime;
        }
    }
}
