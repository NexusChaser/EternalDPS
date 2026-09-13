using System;

namespace NexusChaser.EternalDPS
{
    /// <summary>
    /// The outcome of a load: a verdict, and the value if there is one.
    /// </summary>
    /// <typeparam name="T">What was being loaded.</typeparam>
    /// <remarks>
    /// <para>
    /// Nothing here throws for an expected outcome. A missing file, a save from a newer build and a
    /// failed signature are all things a shipped game runs into, and each one needs a different
    /// screen — not a stack trace.
    /// </para>
    /// <para>
    /// <see cref="RecoveredFromBackup"/> exists because of one rule: recovery is never silent. If
    /// the player has been moved back to an earlier point, the game has to say so, and it can only
    /// do that if the result carries the fact.
    /// </para>
    /// </remarks>
    public readonly struct LoadResult<T>
    {
        private readonly T _value;

        private LoadResult(LoadStatus status, T value, string detail, bool recoveredFromBackup, IntegrityReport integrity)
        {
            Status = status;
            _value = value;
            Detail = detail;
            RecoveredFromBackup = recoveredFromBackup;
            Integrity = integrity;
        }

        /// <summary>The verdict.</summary>
        public LoadStatus Status { get; }

        /// <summary>True only for <see cref="LoadStatus.Ok"/>.</summary>
        public bool IsOk => Status == LoadStatus.Ok;

        /// <summary>
        /// True when the record was not there at all, as opposed to being there and unusable. Worth
        /// distinguishing: the first is a new game, the second is a problem.
        /// </summary>
        public bool IsMissing => Status == LoadStatus.NotFound;

        /// <summary>
        /// The loaded value. Only meaningful when <see cref="IsOk"/>; otherwise it is the default.
        /// Prefer <see cref="TryGetValue"/>, which makes that impossible to get wrong.
        /// </summary>
        public T Value => _value;

        /// <summary>
        /// True when the value came from a backup rather than the primary file. The game must
        /// surface this to the player — they lost whatever happened between the two saves.
        /// </summary>
        public bool RecoveredFromBackup { get; }

        /// <summary>
        /// Present when the verdict came out of a verification pass. Says which region failed and
        /// what the sizes were.
        /// </summary>
        public IntegrityReport Integrity { get; }

        /// <summary>
        /// A sentence for a log. Not a player-facing message, and not something to branch on.
        /// </summary>
        public string Detail { get; }

        /// <summary>The value, if there is one.</summary>
        public bool TryGetValue(out T value)
        {
            value = IsOk ? _value : default;
            return IsOk;
        }

        /// <summary>The value when the load worked, or <paramref name="fallback"/> when it did not.</summary>
        public T ValueOr(T fallback)
        {
            return IsOk ? _value : fallback;
        }

        /// <summary>
        /// Turns the verdict into an exception. For callers that genuinely cannot continue — a
        /// tool, a test — and never on the path a player walks.
        /// </summary>
        /// <exception cref="InvalidOperationException">The load did not succeed.</exception>
        public T ValueOrThrow()
        {
            if (IsOk)
            {
                return _value;
            }

            throw new InvalidOperationException(
                "Load failed with " + Status + (string.IsNullOrEmpty(Detail) ? "." : ": " + Detail));
        }

        /// <summary>
        /// Carries a failure across a type boundary, so a helper that loads bytes can hand its
        /// verdict to a caller that wanted an object without inventing a new one.
        /// </summary>
        /// <exception cref="InvalidOperationException">Called on a successful result.</exception>
        public LoadResult<TOther> CastFailure<TOther>()
        {
            if (IsOk)
            {
                throw new InvalidOperationException("A successful result cannot be cast to another type.");
            }

            return LoadResult.Failed<TOther>(Status, Detail, Integrity);
        }

        public override string ToString()
        {
            return IsOk
                ? "Ok" + (RecoveredFromBackup ? " (from backup)" : string.Empty)
                : Status + (string.IsNullOrEmpty(Detail) ? string.Empty : ": " + Detail);
        }

        internal static LoadResult<T> Create(
            LoadStatus status, T value, string detail, bool recoveredFromBackup, IntegrityReport integrity)
        {
            return new LoadResult<T>(status, value, detail, recoveredFromBackup, integrity);
        }
    }

    /// <summary>
    /// Builds <see cref="LoadResult{T}"/> values. Separate from the generic type so that
    /// <c>LoadResult.Ok(value)</c> infers its type argument instead of making the caller repeat it.
    /// </summary>
    public static class LoadResult
    {
        /// <summary>A successful load.</summary>
        public static LoadResult<T> Ok<T>(T value, bool recoveredFromBackup = false)
        {
            return LoadResult<T>.Create(LoadStatus.Ok, value, null, recoveredFromBackup, null);
        }

        /// <summary>Nothing was there.</summary>
        public static LoadResult<T> NotFound<T>()
        {
            return LoadResult<T>.Create(LoadStatus.NotFound, default, null, false, null);
        }

        /// <summary>A failure of any other kind.</summary>
        /// <exception cref="ArgumentException"><see cref="LoadStatus.Ok"/> was passed.</exception>
        public static LoadResult<T> Failed<T>(LoadStatus status, string detail = null, IntegrityReport integrity = null)
        {
            if (status == LoadStatus.Ok)
            {
                throw new ArgumentException("Ok is not a failure; use LoadResult.Ok instead.", nameof(status));
            }

            return LoadResult<T>.Create(status, default, detail, false, integrity);
        }

        /// <summary>
        /// A failure taken straight from a verification pass, so the region and the sizes travel
        /// with the verdict.
        /// </summary>
        public static LoadResult<T> FromIntegrity<T>(IntegrityReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            if (report.IsValid)
            {
                throw new ArgumentException("A valid report is not a failure.", nameof(report));
            }

            var status = report.Status == IntegrityStatus.MagicMismatch
                ? LoadStatus.NotEternalFile
                : LoadStatus.IntegrityFailed;

            return LoadResult<T>.Create(status, default, report.Detail, false, report);
        }
    }
}
