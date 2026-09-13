using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NexusChaser.EternalDPS.Abstractions;
using UnityEngine;

namespace NexusChaser.EternalDPS.Unity
{
    /// <summary>
    /// Decides when the game actually writes: after a change has settled, and whenever the
    /// application is about to be taken away.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Nothing here hooks <c>OnApplicationQuit</c>, and that is the point.</strong> On
    /// Android the process is killed without it; on WebGL closing the tab does not call it either.
    /// A save system that relies on it works perfectly on the desktop it was built on and loses data
    /// on the two platforms where losing data is hardest to reproduce. There is a second reason as
    /// well: writing is asynchronous, and a write started during teardown may never finish.
    /// </para>
    /// <para>
    /// What is reliable is <c>OnApplicationPause</c> and <c>OnApplicationFocus</c>. On a phone,
    /// pause is what happens when a call arrives or the player switches away — which is the moment
    /// before the process may be killed, and therefore the real last chance.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Eternal/Eternal Save Runner")]
    public sealed class EternalSaveRunner : MonoBehaviour
    {
        private readonly Dictionary<EternalKey, Registration> _sources = new Dictionary<EternalKey, Registration>();
        private readonly List<EternalKey> _writing = new List<EternalKey>();

        private EternalDataDriver _driver;
        private SaveDebouncer _debouncer;
        private IEternalLog _log;

        private sealed class Registration
        {
            public Type ValueType;
            public Func<object> Capture;
            public Func<SaveMetadata> Metadata;
            public SaveProfile? Profile;
        }

        /// <summary>The driver this runner writes through.</summary>
        public EternalDataDriver Driver => _driver;

        /// <summary>True while at least one record is waiting for its quiet period to end.</summary>
        public bool HasPendingChanges => _debouncer != null && !_debouncer.IsIdle;

        /// <summary>
        /// Creates a runner on a hidden object that survives scene loads.
        /// </summary>
        /// <remarks>
        /// It has to outlive scenes: a save triggered by the application being paused during a
        /// scene change would otherwise have nowhere to run.
        /// </remarks>
        public static EternalSaveRunner Create(EternalDataDriver driver, float quietSeconds = 1f, IEternalLog log = null)
        {
            if (driver == null)
            {
                throw new ArgumentNullException(nameof(driver));
            }

            var host = new GameObject("Eternal Save Runner")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };

            DontDestroyOnLoad(host);

            var runner = host.AddComponent<EternalSaveRunner>();
            runner.Initialize(driver, quietSeconds, log);

            return runner;
        }

        /// <summary>Wires an already-built runner to a driver.</summary>
        public void Initialize(EternalDataDriver driver, float quietSeconds = 1f, IEternalLog log = null)
        {
            _driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _debouncer = new SaveDebouncer(quietSeconds);
            _log = log ?? NullLog.Instance;
        }

        /// <summary>
        /// Registers a record and where its current value comes from.
        /// </summary>
        /// <remarks>
        /// The value is read at the moment of writing, not at the moment of the change. That is what
        /// makes debouncing correct: a slider dragged through fifty values writes the fiftieth once,
        /// rather than the first one late.
        /// </remarks>
        /// <param name="key">Which record.</param>
        /// <param name="capture">Reads the current value. Called on the main thread.</param>
        /// <param name="profile">Its policy. Defaults to the preset for its scope and kind.</param>
        /// <param name="metadata">Optional. Called alongside <paramref name="capture"/>.</param>
        public void Track<T>(
            EternalKey key,
            Func<T> capture,
            SaveProfile? profile = null,
            Func<SaveMetadata> metadata = null)
        {
            if (capture == null)
            {
                throw new ArgumentNullException(nameof(capture));
            }

            _sources[key] = new Registration
            {
                ValueType = typeof(T),
                Capture = () => capture(),
                Metadata = metadata,
                Profile = profile,
            };
        }

        /// <summary>Stops tracking a record and forgets any pending change to it.</summary>
        public void Forget(EternalKey key)
        {
            _sources.Remove(key);
            _debouncer?.Cancel(key);
        }

        /// <summary>
        /// Says that a record changed. Safe to call every frame: the countdown restarts each time,
        /// so a gesture becomes one write.
        /// </summary>
        /// <exception cref="InvalidOperationException">The record was never tracked.</exception>
        public void MarkDirty(EternalKey key)
        {
            if (!_sources.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    "Nothing is tracking " + key + ", so there is no way to know what to write. " +
                    "Call Track before marking it dirty.");
            }

            _debouncer.MarkDirty(key);
        }

        /// <summary>
        /// Writes everything pending immediately, without waiting for any quiet period.
        /// </summary>
        /// <remarks>
        /// What the pause and focus hooks call. Await it when the caller can; the hooks cannot,
        /// because Unity's message methods return void.
        /// </remarks>
        public async Task FlushAsync()
        {
            if (_debouncer == null)
            {
                return;
            }

            var due = _debouncer.DrainAll();

            if (due.Count == 0)
            {
                return;
            }

            // Copied out: the list the debouncer returns is reused between calls.
            var keys = new List<EternalKey>(due);

            foreach (var key in keys)
            {
                await WriteAsync(key).ConfigureAwait(false);
            }
        }

        private void Update()
        {
            if (_debouncer == null)
            {
                return;
            }

            // Unscaled, so pausing the game by setting the time scale to zero does not also stop
            // saves from ever being written.
            var due = _debouncer.Tick(Time.unscaledDeltaTime);

            for (var i = 0; i < due.Count; i++)
            {
                StartWrite(due[i]);
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused)
            {
                return;
            }

            // On a phone this is the moment before the process may be killed. There is no later one.
            FlushOnTheWayOut("the application was paused");
        }

        private void OnApplicationFocus(bool focused)
        {
            if (focused)
            {
                return;
            }

            FlushOnTheWayOut("the application lost focus");
        }

        private void FlushOnTheWayOut(string reason)
        {
            if (_debouncer == null || _debouncer.IsIdle)
            {
                return;
            }

            _log.Debug("Writing " + _debouncer.PendingCount + " pending record(s) because " + reason + ".");

            var pending = new List<EternalKey>(_debouncer.DrainAll());

            foreach (var key in pending)
            {
                StartWrite(key);
            }
        }

        /// <summary>
        /// Starts a write and does not wait for it, because the caller is a Unity message that
        /// cannot be awaited. Failures are logged rather than lost.
        /// </summary>
        private async void StartWrite(EternalKey key)
        {
            await WriteAsync(key).ConfigureAwait(false);
        }

        private async Task WriteAsync(EternalKey key)
        {
            if (!_sources.TryGetValue(key, out var registration))
            {
                return;
            }

            if (_writing.Contains(key))
            {
                // A write for this record is already in flight. Starting a second one would have two
                // writers racing for the same file, and which of them landed last would be luck.
                _debouncer.MarkDirty(key);
                return;
            }

            _writing.Add(key);

            try
            {
                var value = registration.Capture();
                var metadata = registration.Metadata?.Invoke();

                await _driver
                    .SaveAsync(key, value, registration.ValueType, metadata, registration.Profile)
                    .ConfigureAwait(false);
            }
            catch (Exception error)
            {
                // Never thrown out of here: this runs from Update and from Unity's own callbacks,
                // where an exception would take the frame down and tell the player nothing.
                _log.Error("Saving " + key.RelativePath + " failed.", error);
            }
            finally
            {
                _writing.Remove(key);
            }
        }

        private void OnDestroy()
        {
            _sources.Clear();
            _debouncer?.Clear();
        }
    }
}
