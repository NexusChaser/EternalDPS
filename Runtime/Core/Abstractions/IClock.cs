using System;

namespace NexusChaser.EternalDPS.Abstractions
{
    /// <summary>
    /// Where the current time comes from.
    /// </summary>
    /// <remarks>
    /// The core never reads the system clock directly. Timestamps end up in the metadata and they
    /// decide which of two saves is newer, so tests have to be able to say "this one was written an
    /// hour later" without waiting an hour.
    /// </remarks>
    public interface IClock
    {
        /// <summary>The current moment, in UTC.</summary>
        DateTimeOffset UtcNow { get; }
    }

    /// <summary>The real clock.</summary>
    public sealed class SystemClock : IClock
    {
        /// <summary>The single shared instance. It has no state.</summary>
        public static readonly SystemClock Instance = new SystemClock();

        /// <inheritdoc />
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
