using System;
using System.Collections.Generic;
using NexusChaser.EternalDPS.Abstractions;

namespace NexusChaser.EternalDPS
{
    /// <summary>
    /// What a build knows how to undo or decode, looked up by the identifier a file names.
    /// </summary>
    /// <typeparam name="T">The kind of implementation being registered.</typeparam>
    /// <remarks>
    /// A registry is per driver, not global. Two systems in the same process — the game and an
    /// editor tool inspecting a foreign save — can then disagree about what is available without
    /// one reaching into the other.
    /// </remarks>
    public abstract class EternalRegistry<T> where T : class
    {
        private readonly Dictionary<byte, T> _entries = new Dictionary<byte, T>();

        /// <summary>How many implementations are registered.</summary>
        public int Count => _entries.Count;

        /// <summary>The identifiers currently registered.</summary>
        public IEnumerable<byte> Ids => _entries.Keys;

        /// <summary>The identifier an implementation declares.</summary>
        protected abstract byte IdOf(T entry);

        /// <summary>What to call this kind of thing in an error message.</summary>
        protected abstract string KindName { get; }

        /// <summary>
        /// Registers an implementation under the identifier it declares.
        /// </summary>
        /// <remarks>
        /// Registering the same instance twice is harmless and does nothing. Registering a
        /// <em>different</em> implementation under an identifier already taken throws, because that
        /// is how a file ends up decoded by something other than what wrote it — silently, and
        /// producing plausible nonsense rather than an error.
        /// </remarks>
        /// <exception cref="ArgumentNullException">No implementation was given.</exception>
        /// <exception cref="ArgumentException">The identifier is zero, or already taken by something else.</exception>
        public void Register(T entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            var id = IdOf(entry);

            if (id == EternalIds.None)
            {
                throw new ArgumentException(
                    "A " + KindName + " cannot use identifier 0: that is what an uninitialised byte looks like.",
                    nameof(entry));
            }

            if (_entries.TryGetValue(id, out var existing))
            {
                if (ReferenceEquals(existing, entry) || existing.GetType() == entry.GetType())
                {
                    return;
                }

                throw new ArgumentException(
                    "Identifier 0x" + id.ToString("X2") + " is already registered to " +
                    existing.GetType().Name + ", and " + entry.GetType().Name + " wants it too. " +
                    "Identifiers are never reused: pick a free one instead.",
                    nameof(entry));
            }

            _entries.Add(id, entry);
        }

        /// <summary>Registers several implementations at once.</summary>
        public void RegisterAll(IEnumerable<T> entries)
        {
            if (entries == null)
            {
                return;
            }

            foreach (var entry in entries)
            {
                Register(entry);
            }
        }

        /// <summary>Looks up an implementation.</summary>
        /// <returns>False when this build does not have it, which is an expected outcome.</returns>
        public bool TryGet(byte id, out T entry)
        {
            return _entries.TryGetValue(id, out entry);
        }

        /// <summary>True when this build knows the identifier.</summary>
        public bool Contains(byte id)
        {
            return _entries.ContainsKey(id);
        }
    }

    /// <summary>The transforms a build can undo.</summary>
    public sealed class TransformRegistry : EternalRegistry<IByteTransform>
    {
        /// <inheritdoc />
        protected override byte IdOf(IByteTransform entry)
        {
            return entry.TransformId;
        }

        /// <inheritdoc />
        protected override string KindName => "transform";

        /// <summary>A registry holding the transforms the package itself provides.</summary>
        public static TransformRegistry WithBuiltIns()
        {
            var registry = new TransformRegistry();
            registry.Register(new Transforms.DeflateTransform());
            return registry;
        }
    }

    /// <summary>The serialisers a build can decode with.</summary>
    public sealed class SerializerRegistry : EternalRegistry<ISerializer>
    {
        /// <inheritdoc />
        protected override byte IdOf(ISerializer entry)
        {
            return entry.FormatId;
        }

        /// <inheritdoc />
        protected override string KindName => "serializer";
    }
}
