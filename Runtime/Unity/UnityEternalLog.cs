using System;
using NexusChaser.EternalDPS.Abstractions;
using UnityEngine;

namespace NexusChaser.EternalDPS.Unity
{
    /// <summary>
    /// Sends the save system's log to the Unity console.
    /// </summary>
    /// <remarks>
    /// The core cannot call <c>Debug.Log</c> itself: it has no reference to the engine, and that is
    /// the property that lets the whole suite run in CI without opening Unity. This is the adapter
    /// that puts it back for a game.
    /// </remarks>
    public sealed class UnityEternalLog : IEternalLog
    {
        private const string Prefix = "[Eternal] ";

        /// <summary>Creates a log.</summary>
        /// <param name="minimumLevel">
        /// Entries below this are discarded. Defaults to hiding debug detail, which is noise in a
        /// shipped build and in the console of anyone who is not working on saving.
        /// </param>
        /// <param name="context">
        /// Optional object to highlight in the hierarchy when an entry is clicked.
        /// </param>
        public UnityEternalLog(EternalLogLevel minimumLevel = EternalLogLevel.Info, UnityEngine.Object context = null)
        {
            MinimumLevel = minimumLevel;
            Context = context;
        }

        /// <summary>The level below which entries are dropped.</summary>
        public EternalLogLevel MinimumLevel { get; set; }

        /// <summary>What the console highlights when an entry is clicked.</summary>
        public UnityEngine.Object Context { get; set; }

        /// <inheritdoc />
        public void Log(EternalLogLevel level, string message, Exception exception)
        {
            if (level < MinimumLevel)
            {
                return;
            }

            var text = Prefix + message;

            switch (level)
            {
                case EternalLogLevel.Error:
                    Debug.LogError(text, Context);

                    if (exception != null)
                    {
                        Debug.LogException(exception, Context);
                    }

                    break;

                case EternalLogLevel.Warning:
                    Debug.LogWarning(text, Context);
                    break;

                default:
                    Debug.Log(text, Context);
                    break;
            }
        }
    }
}
