using System;

namespace NexusChaser.EternalDPS.Abstractions
{
    /// <summary>How serious a log entry is.</summary>
    public enum EternalLogLevel
    {
        /// <summary>Detail for chasing a problem. Off in a shipped build.</summary>
        Debug = 0,

        /// <summary>Something normal worth recording, such as a save completing.</summary>
        Info = 1,

        /// <summary>Something recovered from, such as falling back to a backup.</summary>
        Warning = 2,

        /// <summary>Something that failed and could not be recovered from.</summary>
        Error = 3,
    }

    /// <summary>
    /// Where the system writes what it did.
    /// </summary>
    /// <remarks>
    /// The core cannot call <c>Debug.Log</c>: it does not reference the engine, and that is the
    /// property that lets the whole test suite run in CI without opening Unity. The Unity layer
    /// supplies an adapter; a server or a command line tool supplies its own.
    /// </remarks>
    public interface IEternalLog
    {
        /// <summary>Writes an entry.</summary>
        /// <param name="level">How serious it is.</param>
        /// <param name="message">What happened.</param>
        /// <param name="exception">The exception behind it, when there is one.</param>
        void Log(EternalLogLevel level, string message, Exception exception);
    }

    /// <summary>A log that discards everything. The default, so nothing has to null-check.</summary>
    public sealed class NullLog : IEternalLog
    {
        /// <summary>The single shared instance. It has no state.</summary>
        public static readonly NullLog Instance = new NullLog();

        /// <inheritdoc />
        public void Log(EternalLogLevel level, string message, Exception exception)
        {
        }
    }

    /// <summary>Shorthand for the common cases.</summary>
    public static class EternalLogExtensions
    {
        /// <summary>Writes a debug entry.</summary>
        public static void Debug(this IEternalLog log, string message)
        {
            log?.Log(EternalLogLevel.Debug, message, null);
        }

        /// <summary>Writes an informational entry.</summary>
        public static void Info(this IEternalLog log, string message)
        {
            log?.Log(EternalLogLevel.Info, message, null);
        }

        /// <summary>Writes a warning.</summary>
        public static void Warning(this IEternalLog log, string message)
        {
            log?.Log(EternalLogLevel.Warning, message, null);
        }

        /// <summary>Writes an error, with the exception behind it when there is one.</summary>
        public static void Error(this IEternalLog log, string message, Exception exception = null)
        {
            log?.Log(EternalLogLevel.Error, message, exception);
        }
    }
}
