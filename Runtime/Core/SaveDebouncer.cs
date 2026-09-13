using System;
using System.Collections.Generic;

namespace NexusChaser.EternalDPS
{
    /// <summary>
    /// Collects "this changed" notices and decides when they become an actual write.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A volume slider raises a change every frame it is dragged. Writing on each one means a
    /// hundred serialise-compress-sign-write cycles for a gesture that ends in a single value, and
    /// on a phone that is felt. This waits for the change to stop and then writes once.
    /// </para>
    /// <para>
    /// Engine-free on purpose: it counts elapsed seconds handed to it from outside rather than
    /// reading a clock or a frame. That makes the timing testable without a running game — the
    /// tests step it in whatever increments they like — and it means another engine's layer only
    /// has to supply its own delta time.
    /// </para>
    /// </remarks>
    public sealed class SaveDebouncer
    {
        private readonly Dictionary<EternalKey, float> _pending = new Dictionary<EternalKey, float>();
        private readonly List<EternalKey> _due = new List<EternalKey>();
        private readonly List<EternalKey> _scratch = new List<EternalKey>();

        /// <summary>Creates a debouncer.</summary>
        /// <param name="quietSeconds">
        /// How long a record has to go unchanged before it is written. Long enough that a dragged
        /// slider writes once, short enough that a player who quits immediately afterwards does not
        /// lose the change.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">The delay is negative.</exception>
        public SaveDebouncer(float quietSeconds = 1f)
        {
            if (quietSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(quietSeconds), quietSeconds, "A quiet period cannot be negative.");
            }

            QuietSeconds = quietSeconds;
        }

        /// <summary>How long a record waits after its last change.</summary>
        public float QuietSeconds { get; }

        /// <summary>How many records are waiting to be written.</summary>
        public int PendingCount => _pending.Count;

        /// <summary>True when nothing is waiting.</summary>
        public bool IsIdle => _pending.Count == 0;

        /// <summary>
        /// Notes that a record changed. Called as often as it likes; the countdown restarts each
        /// time, which is what turns a drag into one write.
        /// </summary>
        public void MarkDirty(EternalKey key)
        {
            _pending[key] = QuietSeconds;
        }

        /// <summary>True when this record is waiting to be written.</summary>
        public bool IsPending(EternalKey key)
        {
            return _pending.ContainsKey(key);
        }

        /// <summary>
        /// Advances the countdowns and returns the records whose quiet period has elapsed.
        /// </summary>
        /// <param name="deltaSeconds">Time since the last call. Negative values are ignored.</param>
        /// <remarks>
        /// The returned list is reused between calls; read it before calling again. Doing so keeps
        /// a per-frame call from allocating, which is the whole reason this is not written with LINQ.
        /// </remarks>
        public IReadOnlyList<EternalKey> Tick(float deltaSeconds)
        {
            _due.Clear();

            if (_pending.Count == 0)
            {
                return _due;
            }

            if (deltaSeconds < 0f)
            {
                deltaSeconds = 0f;
            }

            _scratch.Clear();

            foreach (var entry in _pending)
            {
                _scratch.Add(entry.Key);
            }

            foreach (var key in _scratch)
            {
                var remaining = _pending[key] - deltaSeconds;

                if (remaining > 0f)
                {
                    _pending[key] = remaining;
                    continue;
                }

                _pending.Remove(key);
                _due.Add(key);
            }

            return _due;
        }

        /// <summary>
        /// Gives up waiting and returns everything pending at once.
        /// </summary>
        /// <remarks>
        /// What happens when the application is going away. At that point the point of waiting has
        /// evaporated: there may be no more frames, so a countdown that has not finished never will.
        /// </remarks>
        public IReadOnlyList<EternalKey> DrainAll()
        {
            _due.Clear();

            foreach (var entry in _pending)
            {
                _due.Add(entry.Key);
            }

            _pending.Clear();

            return _due;
        }

        /// <summary>Forgets a pending change without writing it.</summary>
        public void Cancel(EternalKey key)
        {
            _pending.Remove(key);
        }

        /// <summary>Forgets everything pending without writing any of it.</summary>
        public void Clear()
        {
            _pending.Clear();
        }
    }
}
