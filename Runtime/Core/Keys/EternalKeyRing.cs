using System;
using System.Collections.Generic;
using System.Text;

namespace NexusChaser.EternalDPS.Keys
{
    /// <summary>
    /// A game's keys: the current one, and every retired one still needed to read older saves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// There is a real tension between two things that are both wanted. A key that never changes is
    /// exactly what lets an old save keep loading, and exactly what makes a leak permanent. Picking
    /// a side does not resolve it — versioning the key does. Sign with the newest, verify with
    /// whichever the file names, and a leaked key stops being worth anything the moment the next
    /// version ships, without stranding anybody.
    /// </para>
    /// <para>
    /// The package supplies the ring; the game supplies what goes in it. Nothing here contains a
    /// key, because this repository is public and a key inside it would be published with it.
    /// </para>
    /// </remarks>
    public sealed class EternalKeyRing : IKeyProvider
    {
        private readonly Dictionary<byte, EternalKeyMaterial> _byId = new Dictionary<byte, EternalKeyMaterial>();
        private readonly EternalKeyMaterial _signing;

        /// <summary>
        /// Assembles a ring, checking the invariants that make rotation safe.
        /// </summary>
        /// <remarks>
        /// Everything here is checked at construction so that a badly assembled ring fails at
        /// startup, on the developer's machine, rather than the first time a player's save will not
        /// load.
        /// </remarks>
        /// <exception cref="ArgumentException">The ring is empty or holds two keys with the same identifier.</exception>
        /// <exception cref="EternalKeyMissingException">There is no active key, or more than one.</exception>
        public EternalKeyRing(params EternalKeyMaterial[] keys)
        {
            if (keys == null || keys.Length == 0)
            {
                throw new ArgumentException("A key ring needs at least one key.", nameof(keys));
            }

            EternalKeyMaterial active = null;
            byte highestId = 0;

            foreach (var key in keys)
            {
                if (key == null)
                {
                    throw new ArgumentException("The ring contains a null key.", nameof(keys));
                }

                if (_byId.ContainsKey(key.Id))
                {
                    // The same rule as every other identifier in this format: a number means one
                    // thing forever. Two keys sharing one would make a file's keyId ambiguous.
                    throw new ArgumentException(
                        "Two keys claim identifier " + key.Id +
                        ". A key identifier is never reused, not even for a replacement.",
                        nameof(keys));
                }

                _byId.Add(key.Id, key);

                if (key.Id > highestId)
                {
                    highestId = key.Id;
                }

                if (!key.CanSign)
                {
                    continue;
                }

                if (active != null)
                {
                    throw new EternalKeyMissingException(
                        "The ring holds two active keys (" + active.Id + " and " + key.Id +
                        "), so which one signs would depend on ordering. Exactly one key is active; " +
                        "the rest are retired.");
                }

                active = key;
            }

            if (active == null)
            {
                throw new EternalKeyMissingException(
                    "The ring holds no active key, so nothing could be signed. Retiring the last " +
                    "active key without adding a replacement leaves a build that can read saves and " +
                    "never write one.");
            }

            if (active.Id != highestId)
            {
                // Rotation only ever moves forward. Tools pick the next free identifier, and a
                // reader seeing a higher number than the active one would mean a key went missing.
                throw new EternalKeyMissingException(
                    "The active key is " + active.Id + " but the ring also holds " + highestId +
                    ". Rotation always moves to a higher identifier, so the active key is the highest one.");
            }

            _signing = active;
        }

        /// <summary>How many keys the ring holds, active and retired.</summary>
        public int Count => _byId.Count;

        /// <summary>The identifier of the key new saves are signed with.</summary>
        public byte SigningKeyId => _signing.Id;

        /// <inheritdoc />
        public EternalKeyMaterial GetSigningKey()
        {
            return _signing;
        }

        /// <inheritdoc />
        public bool TryGetKey(byte keyId, out EternalKeyMaterial key)
        {
            return _byId.TryGetValue(keyId, out key);
        }

        /// <summary>
        /// The identifier a newly generated key should take: one past the highest in the ring.
        /// </summary>
        /// <remarks>
        /// Identifiers are a single byte and zero is reserved, so a game can rotate 255 times. That
        /// ceiling is not a practical limit — it is roughly one rotation per release for a couple of
        /// decades — but when it is reached this says so instead of wrapping round and quietly
        /// reusing identifier 1, which would make old files verify against the wrong key.
        /// </remarks>
        /// <exception cref="EternalKeyMissingException">Every identifier is spent.</exception>
        public byte NextFreeKeyId()
        {
            if (_signing.Id == EternalIds.GameRangeLast)
            {
                throw new EternalKeyMissingException(
                    "Key identifier 255 is in use and there is no higher one. The container reserves " +
                    "a single byte for it, so a new identity is needed rather than a further rotation.");
            }

            return (byte)(_signing.Id + 1);
        }

        public override string ToString()
        {
            var text = new StringBuilder("key ring: ");
            var first = true;

            foreach (var key in _byId.Values)
            {
                if (!first)
                {
                    text.Append(", ");
                }

                text.Append(key);
                first = false;
            }

            return text.ToString();
        }
    }
}
