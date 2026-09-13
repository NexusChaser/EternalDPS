using System;
using System.Collections.Generic;
using NexusChaser.EternalDPS.Abstractions;
using NexusChaser.EternalDPS.Keys;
using NexusChaser.EternalDPS.Transforms;
using UnityEngine;

namespace NexusChaser.EternalDPS.Unity
{
    /// <summary>
    /// What a game has to supply to start the save system, and what it may override.
    /// </summary>
    public sealed class EternalUnitySetup
    {
        /// <summary>
        /// Who this game is: a GUID generated once and frozen for the life of the product, plus any
        /// identity it used to write under. Required.
        /// </summary>
        public ProductIdentity Identity { get; set; }

        /// <summary>
        /// The game's signing keys. Required.
        /// </summary>
        /// <remarks>
        /// The package ships none and never will — it lives in a public repository, so any key in it
        /// would be published alongside it and would protect nothing. Build one with
        /// <see cref="EternalKeyRing"/> from material that is not a literal string in the source.
        /// </remarks>
        public IKeyProvider Keys { get; set; }

        /// <summary>The shape of this build's data, for migrations.</summary>
        public uint SchemaVersion { get; set; } = 1;

        /// <summary>
        /// What turns objects into bytes. Left null, the JSON adapter is used when it is available.
        /// </summary>
        public ISerializer Serializer { get; set; }

        /// <summary>
        /// Where bytes go. Left null, a file store over <see cref="EternalPaths.DataRoot"/> is used.
        /// </summary>
        public IStore Store { get; set; }

        /// <summary>Optional folder under the data root, to keep saves apart from other files.</summary>
        public string SubFolder { get; set; }

        /// <summary>Every record the game will use, so conflicts are found at startup.</summary>
        public IReadOnlyList<EternalKey> Records { get; set; }

        /// <summary>Lowest log level that reaches the console.</summary>
        public EternalLogLevel LogLevel { get; set; } = EternalLogLevel.Info;

        /// <summary>How long a record waits after its last change before being written.</summary>
        public float AutoSaveQuietSeconds { get; set; } = 1f;

        /// <summary>
        /// False writes unsigned saves. Development only, and every save says so in the console.
        /// </summary>
        public bool SignSaves { get; set; } = true;

        /// <summary>
        /// Re-sign a record onto the current key as soon as it is read, rather than waiting for the
        /// next ordinary save.
        /// </summary>
        public bool AutoResign { get; set; } = true;
    }

    /// <summary>
    /// Builds a configured save system for a Unity game.
    /// </summary>
    /// <remarks>
    /// Everything optional has a default that is correct for the common case, and everything that
    /// cannot have one — who the game is, and what signs its saves — has to be supplied. The
    /// failures here are loud and immediate, because a save system that silently starts up wrong is
    /// discovered by a player and not by a developer.
    /// </remarks>
    public static class EternalUnity
    {
        /// <summary>
        /// Assembles the driver: store, serializer, transforms, keys, log and clock.
        /// </summary>
        /// <exception cref="ArgumentNullException">The identity or the keys are missing.</exception>
        /// <exception cref="InvalidOperationException">
        /// No serializer was given and none is compiled into this build.
        /// </exception>
        public static EternalDataDriver CreateDriver(EternalUnitySetup setup)
        {
            if (setup == null)
            {
                throw new ArgumentNullException(nameof(setup));
            }

            if (setup.Identity == null)
            {
                throw new ArgumentNullException(
                    nameof(setup.Identity),
                    "The save system needs a product identity: a GUID generated once and frozen for " +
                    "the life of the game. It is what lets saves survive the product being renamed.");
            }

            if (setup.SignSaves && setup.Keys == null)
            {
                throw new ArgumentNullException(
                    nameof(setup.Keys),
                    "Signed saves need a key provider. This package deliberately ships no key: it is " +
                    "a public repository, so any key inside it would be published with it.");
            }

            var log = new UnityEternalLog(setup.LogLevel);
            var store = setup.Store ?? CreateDefaultStore(setup, log);
            var serializer = setup.Serializer ?? CreateDefaultSerializer();

            var transforms = TransformRegistry.WithBuiltIns();

            return new EternalDataDriver(new EternalDataDriverOptions
            {
                Store = store,
                Serializer = serializer,
                Keys = setup.Keys,
                Identity = setup.Identity,
                SchemaVersion = setup.SchemaVersion,
                WriteTransforms = new IByteTransform[] { new DeflateTransform() },
                Transforms = transforms,
                AppVersion = Application.version,
                Clock = SystemClock.Instance,
                Log = log,
                Records = setup.Records,
                SignSaves = setup.SignSaves,
                AutoResign = setup.AutoResign,
            });
        }

        private static IStore CreateDefaultStore(EternalUnitySetup setup, IEternalLog log)
        {
            if (EternalPaths.NeedsPlatformStore)
            {
                // Not a warning: a file store on WebGL looks like it works and loses everything when
                // the tab closes, because Unity never flushes the virtual filesystem to IndexedDB on
                // its own. Failing here is the only way that does not reach a player.
                throw new InvalidOperationException(
                    "This platform needs a store of its own and the browser one is not built yet. " +
                    "A plain file store here would appear to save and would lose everything when the " +
                    "tab is closed. Supply Store explicitly if you know what you are doing.");
            }

            log.Debug("Saves are kept in " + EternalPaths.DataRoot + ".");

            return EternalPaths.CreateFileStore(setup.SubFolder);
        }

        private static ISerializer CreateDefaultSerializer()
        {
#if ETERNAL_NEWTONSOFT
            return new Serialization.NewtonsoftJsonSerializer();
#else
            throw new InvalidOperationException(
                "No serializer was supplied and the JSON adapter is not compiled into this build. " +
                "Either add the com.unity.nuget.newtonsoft-json package, or set Serializer yourself.");
#endif
        }
    }
}
