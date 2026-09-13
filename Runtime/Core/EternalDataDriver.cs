using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NexusChaser.EternalDPS.Abstractions;
using NexusChaser.EternalDPS.Container;
using NexusChaser.EternalDPS.Keys;

namespace NexusChaser.EternalDPS
{
    /// <summary>
    /// Everything the driver needs to work. Assembled once, at startup.
    /// </summary>
    public sealed class EternalDataDriverOptions
    {
        /// <summary>Where bytes are kept. Required.</summary>
        public IStore Store { get; set; }

        /// <summary>What turns objects into bytes. Required.</summary>
        public ISerializer Serializer { get; set; }

        /// <summary>Where the keys come from. Required unless <see cref="SignSaves"/> is false.</summary>
        public IKeyProvider Keys { get; set; }

        /// <summary>Who this game is. Required.</summary>
        public ProductIdentity Identity { get; set; }

        /// <summary>The shape of the game's data as this build writes it.</summary>
        public uint SchemaVersion { get; set; } = 1;

        /// <summary>
        /// Applied to the body in this order on the way out. Defaults to Deflate alone, which is
        /// the profile the architecture calls for together with the signature.
        /// </summary>
        public IReadOnlyList<IByteTransform> WriteTransforms { get; set; }

        /// <summary>
        /// What this build can undo when reading. Defaults to the transforms the package provides.
        /// A file naming anything absent from here loads as
        /// <see cref="LoadStatus.UnknownTransform"/> rather than crashing.
        /// </summary>
        public TransformRegistry Transforms { get; set; }

        /// <summary>Build identifier recorded in every save, for support.</summary>
        public string AppVersion { get; set; }

        /// <summary>Where timestamps come from. Defaults to the system clock.</summary>
        public IClock Clock { get; set; }

        /// <summary>Where the system reports what it did. Defaults to discarding it.</summary>
        public IEternalLog Log { get; set; }

        /// <summary>
        /// False writes unsigned saves. Only ever for a development build, and it is logged as a
        /// warning every time a save is written so that it cannot go unnoticed.
        /// </summary>
        public bool SignSaves { get; set; } = true;

        /// <summary>
        /// Every record the game intends to use, declared up front so that mistakes are found at
        /// startup instead of in front of a player.
        /// </summary>
        /// <remarks>
        /// Optional. Records not declared here are still checked the first time they are used, so
        /// declaring them buys one thing: the failure happens during startup rather than whenever
        /// the game first happens to touch that record.
        /// </remarks>
        public IReadOnlyList<EternalKey> Records { get; set; }

        /// <summary>
        /// Re-sign a record with the current key as soon as it is read, when it was signed with a
        /// retired one.
        /// </summary>
        /// <remarks>
        /// On by default, and silent: the player does nothing and sees nothing. Waiting for the
        /// game to happen to save that record would leave a leaked key useful against everything
        /// the player only ever reads — account progress that changes once a month, for instance.
        /// Turning this off falls back to re-signing on the next ordinary save, which happens
        /// anyway because every save uses the current key.
        /// </remarks>
        public bool AutoResign { get; set; } = true;
    }

    /// <summary>
    /// Thrown when two records would end up in the same file.
    /// </summary>
    /// <remarks>
    /// The layout has no segment for the record kind, so two keys that agree on scope, slot and
    /// record id land on one file however different their policies are. This is thrown rather than
    /// worked around because the alternative is one record silently overwriting the other, which
    /// surfaces as lost progress weeks later.
    /// </remarks>
    public sealed class EternalRecordConflictException : Exception
    {
        /// <summary>Creates the exception naming both records.</summary>
        public EternalRecordConflictException(EternalKey first, EternalKey second)
            : base("Records " + first + " and " + second + " both resolve to '" + first.RelativePath +
                   "'. The path does not encode the record kind, so a record id has to be unique " +
                   "within its scope even across kinds. Rename one of them.")
        {
            First = first;
            Second = second;
        }

        /// <summary>The record already registered.</summary>
        public EternalKey First { get; }

        /// <summary>The record that collided with it.</summary>
        public EternalKey Second { get; }
    }

    /// <summary>
    /// The front door: save, load, delete, check. Orchestrates the serialiser, the transform chain,
    /// the container and the store, and turns everything that can go wrong into a verdict.
    /// </summary>
    /// <remarks>
    /// Nothing here throws for an outcome a shipped game will meet. A missing save, a save from a
    /// newer build, a broken signature and a foreign file are all answers, not exceptions. What
    /// does throw is a mistake by whoever wired the system up, because that has to be found before
    /// the build leaves the machine it was made on.
    /// </remarks>
    public sealed class EternalDataDriver
    {
        private readonly IStore _store;
        private readonly ISerializer _serializer;
        private readonly IKeyProvider _keys;
        private readonly ProductIdentity _identity;
        private readonly uint _schemaVersion;
        private readonly IReadOnlyList<IByteTransform> _writeTransforms;
        private readonly TransformRegistry _transforms;
        private readonly string _appVersion;
        private readonly IClock _clock;
        private readonly IEternalLog _log;
        private readonly bool _sign;
        private readonly bool _autoResign;

        private readonly object _recordLock = new object();
        private readonly Dictionary<string, EternalKey> _knownRecords = new Dictionary<string, EternalKey>(StringComparer.Ordinal);

        /// <summary>Builds a driver.</summary>
        /// <exception cref="ArgumentNullException">A required collaborator is missing.</exception>
        public EternalDataDriver(EternalDataDriverOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            _store = options.Store ?? throw new ArgumentNullException(nameof(options.Store));
            _serializer = options.Serializer ?? throw new ArgumentNullException(nameof(options.Serializer));
            _identity = options.Identity ?? throw new ArgumentNullException(nameof(options.Identity));

            _sign = options.SignSaves;
            _keys = options.Keys;

            if (_sign && _keys == null)
            {
                throw new ArgumentNullException(
                    nameof(options.Keys),
                    "Signed saves need a key provider. Set SignSaves to false only if this build is " +
                    "genuinely meant to write saves that verify nothing.");
            }

            _schemaVersion = options.SchemaVersion;
            _writeTransforms = options.WriteTransforms ?? new IByteTransform[] { new Transforms.DeflateTransform() };
            _transforms = options.Transforms ?? TransformRegistry.WithBuiltIns();
            _appVersion = options.AppVersion ?? string.Empty;
            _clock = options.Clock ?? SystemClock.Instance;
            _log = options.Log ?? NullLog.Instance;
            _autoResign = options.AutoResign;

            if (options.Records == null)
            {
                return;
            }

            foreach (var record in options.Records)
            {
                RegisterRecord(record);
            }
        }

        /// <summary>
        /// Checks a record and remembers it, so that a conflict or a questionable scope is reported
        /// once rather than every time.
        /// </summary>
        /// <exception cref="EternalRecordConflictException">Another record already claims that file.</exception>
        private void RegisterRecord(EternalKey record)
        {
            lock (_recordLock)
            {
                if (_knownRecords.TryGetValue(record.RelativePath, out var existing))
                {
                    if (existing.ConflictsWith(record))
                    {
                        throw new EternalRecordConflictException(existing, record);
                    }

                    return;
                }

                _knownRecords.Add(record.RelativePath, record);
            }

            // Advisory, never blocking: a game may have a reason the matrix did not anticipate. But
            // machine-scoped progress is progress that evaporates when the player changes computer,
            // and accepting that in total silence is how they find out the hard way.
            if (!ScopeRules.IsMeaningful(record.Scope, record.Kind))
            {
                _log.Warning(
                    record.Kind + " in the " + record.Scope + " scope (" + record.RelativePath +
                    ") is outside the combinations the design accounts for. It will work; make sure " +
                    "it is deliberate.");
            }
        }

        /// <summary>Whether a record exists, without reading or checking it.</summary>
        public Task<bool> ExistsAsync(EternalKey key, CancellationToken cancellationToken = default)
        {
            RegisterRecord(key);
            return _store.ExistsAsync(key.RelativePath, cancellationToken);
        }

        /// <summary>
        /// Removes a record. Removing something that is not there succeeds: the caller asked for it
        /// to be gone, and it is.
        /// </summary>
        public async Task DeleteAsync(EternalKey key, CancellationToken cancellationToken = default)
        {
            RegisterRecord(key);

            await _store.DeleteAsync(key.RelativePath, cancellationToken).ConfigureAwait(false);
            await _store.FlushAsync(cancellationToken).ConfigureAwait(false);

            _log.Info("Deleted " + key.RelativePath + ".");
        }

        /// <summary>
        /// Checks a record's signature without decoding it. Cheap enough to run over everything.
        /// </summary>
        public async Task<IntegrityReport> VerifyAsync(EternalKey key, CancellationToken cancellationToken = default)
        {
            RegisterRecord(key);

            var bytes = await _store.ReadAsync(key.RelativePath, cancellationToken).ConfigureAwait(false);

            if (bytes == null)
            {
                return IntegrityReport.Failed(
                    IntegrityStatus.Truncated, ContainerRegion.None, "There is no such record.");
            }

            return ContainerReader.Verify(bytes, _keys);
        }

        /// <summary>
        /// Reads a record's metadata without reading its contents. What a slot list is built from.
        /// </summary>
        /// <param name="key">Which record.</param>
        /// <param name="includeThumbnail">False to skip the preview image.</param>
        /// <param name="cancellationToken">Cancellation.</param>
        public async Task<LoadResult<SaveMetadata>> ReadMetadataAsync(
            EternalKey key, bool includeThumbnail = true, CancellationToken cancellationToken = default)
        {
            RegisterRecord(key);

            var bytes = await _store.ReadAsync(key.RelativePath, cancellationToken).ConfigureAwait(false);

            return bytes == null
                ? LoadResult.NotFound<SaveMetadata>()
                : ContainerReader.ReadMetadata(bytes, includeThumbnail);
        }

        /// <summary>
        /// Writes a record: serialise, transform, wrap, sign, store, flush.
        /// </summary>
        /// <param name="key">Where it goes.</param>
        /// <param name="value">What to write.</param>
        /// <param name="metadata">
        /// Optional. The product id, schema version and timestamp are filled in here regardless of
        /// what the caller set, because those three are the system's to maintain and a game getting
        /// them wrong is how a save becomes unidentifiable.
        /// </param>
        /// <param name="cancellationToken">Cancellation.</param>
        public async Task SaveAsync<T>(
            EternalKey key,
            T value,
            SaveMetadata metadata = null,
            CancellationToken cancellationToken = default)
        {
            RegisterRecord(key);

            var stamped = metadata?.Clone() ?? new SaveMetadata();

            stamped.ProductId = _identity.Current;
            stamped.SchemaVersion = _schemaVersion;
            stamped.SavedAtUtc = _clock.UtcNow;

            if (string.IsNullOrEmpty(stamped.AppVersion))
            {
                stamped.AppVersion = _appVersion;
            }

            var body = _serializer.Serialize(value, typeof(T));

            byte[] file;

            if (_sign)
            {
                file = ContainerWriter.Write(body, _serializer.FormatId, _writeTransforms, stamped, _keys);
            }
            else
            {
                // Said out loud every single time. A build that writes unsigned saves looks
                // identical from the outside to one that does not.
                _log.Warning("Writing " + key.RelativePath + " UNSIGNED. This must never reach players.");
                file = ContainerWriter.WriteUnsigned(body, _serializer.FormatId, _writeTransforms, stamped);
            }

            await _store.WriteAsync(key.RelativePath, file, cancellationToken).ConfigureAwait(false);
            await _store.FlushAsync(cancellationToken).ConfigureAwait(false);

            _log.Info("Saved " + key.RelativePath + " (" + file.Length + " bytes).");
        }

        /// <summary>
        /// Reads a record, applying the profile's policy when something is wrong with it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The verdict says what happened; the profile decides what to do about it. The same broken
        /// signature means "try the backup" for progress and "discard it and tell the player" for a
        /// session, and that difference lives in the profile rather than in two copies of this
        /// method.
        /// </para>
        /// <para>
        /// When a value does come back from a backup, the result says so. Recovering in silence
        /// would leave the player wondering why they lost the last half hour.
        /// </para>
        /// </remarks>
        /// <param name="key">Which record.</param>
        /// <param name="profile">Its policy. Defaults to the preset for its scope and kind.</param>
        /// <param name="cancellationToken">Cancellation.</param>
        public async Task<LoadResult<T>> LoadAsync<T>(
            EternalKey key,
            SaveProfile? profile = null,
            CancellationToken cancellationToken = default)
        {
            RegisterRecord(key);

            var policy = profile ?? SaveProfile.For(key.Scope, key.Kind);

            var attempt = await LoadFromPathAsync<T>(key.RelativePath, cancellationToken).ConfigureAwait(false);
            var primary = attempt.Result;

            if (primary.IsOk)
            {
                await AfterSuccessfulLoadAsync(key, attempt.Report, cancellationToken).ConfigureAwait(false);
                return primary;
            }

            if (primary.IsMissing)
            {
                return primary;
            }

            // A save from a newer build is not damage and must not be replaced by an older backup:
            // the player would lose everything they did in the newer one.
            if (primary.Status == LoadStatus.ContainerTooNew || primary.Status == LoadStatus.SchemaTooNew)
            {
                _log.Warning("Refusing to touch " + key.RelativePath + ": " + primary.Status + ".");
                return primary;
            }

            var isIntegrityFailure = primary.Status == LoadStatus.IntegrityFailed
                                     || primary.Status == LoadStatus.Corrupt;

            if (isIntegrityFailure && policy.OnIntegrityFailure == IntegrityFailurePolicy.TryBackup)
            {
                for (var generation = 1; generation <= Math.Max(policy.Backups, 1); generation++)
                {
                    var backupPath = key.RelativeBackupPath(generation);
                    var backup = (await LoadFromPathAsync<T>(backupPath, cancellationToken).ConfigureAwait(false)).Result;

                    if (!backup.IsOk)
                    {
                        continue;
                    }

                    _log.Warning(
                        "Recovered " + key.RelativePath + " from " + backupPath +
                        ". The player has been moved back to an earlier point and has to be told.");

                    return LoadResult.Ok(backup.Value, recoveredFromBackup: true);
                }

                _log.Error("No usable backup for " + key.RelativePath + ": " + primary.Detail);
            }

            if (policy.OnIntegrityFailure == IntegrityFailurePolicy.DiscardAndNotify && isIntegrityFailure)
            {
                _log.Warning("Discarding " + key.RelativePath + " as its policy requires: " + primary.Status + ".");
            }

            return primary;
        }

        /// <summary>
        /// Re-signs a record with the current key if it is still carrying a retired one.
        /// </summary>
        /// <remarks>
        /// Nothing inside the save changes: the metadata and the body come out byte for byte
        /// identical, and only the key identifier and the signature are rewritten. The record is
        /// verified with its old key first, so a file somebody edited cannot be laundered into a
        /// validly signed one.
        /// </remarks>
        /// <returns>True when the record was rewritten.</returns>
        public async Task<bool> ResignAsync(EternalKey key, CancellationToken cancellationToken = default)
        {
            RegisterRecord(key);

            return await ResignPathAsync(key.RelativePath, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Everything that happens after a record has loaded cleanly: say something if its key is
        /// on its way out, and move it onto the current key.
        /// </summary>
        private async Task AfterSuccessfulLoadAsync(
            EternalKey key, IntegrityReport report, CancellationToken cancellationToken)
        {
            if (report == null || !report.SignedWithRetiringKey)
            {
                return;
            }

            // The whole reason Warn exists as a separate stage. Without this it behaves exactly
            // like ReadOnly and the stage means nothing.
            if (report.SigningKeyState == KeyState.Warn)
            {
                _log.Warning(
                    key.RelativePath + " is signed with key " + report.KeyId +
                    ", which is being retired. It still verifies, and it will be re-signed.");
            }

            if (!_autoResign || !_sign)
            {
                return;
            }

            try
            {
                await ResignPathAsync(key.RelativePath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                // Re-signing is housekeeping. A record that loaded correctly must not come back as
                // a failure because the tidying afterwards did not work.
                _log.Warning("Could not re-sign " + key.RelativePath + ": " + error.Message);
            }
        }

        private async Task<bool> ResignPathAsync(string path, CancellationToken cancellationToken)
        {
            if (!_sign)
            {
                return false;
            }

            var bytes = await _store.ReadAsync(path, cancellationToken).ConfigureAwait(false);

            if (bytes == null)
            {
                return false;
            }

            if (!ContainerWriter.TryResign(bytes, _keys, out var resigned, out var report))
            {
                if (!report.IsValid)
                {
                    _log.Warning("Not re-signing " + path + " because it does not verify: " + report.Status + ".");
                }

                return false;
            }

            await _store.WriteAsync(path, resigned, cancellationToken).ConfigureAwait(false);
            await _store.FlushAsync(cancellationToken).ConfigureAwait(false);

            // Deliberately not a warning. The player did nothing and needs to do nothing.
            _log.Info("Re-signed " + path + " with the current key.");

            return true;
        }

        private async Task<(LoadResult<T> Result, IntegrityReport Report)> LoadFromPathAsync<T>(
            string path, CancellationToken cancellationToken)
        {
            var bytes = await _store.ReadAsync(path, cancellationToken).ConfigureAwait(false);

            if (bytes == null)
            {
                return (LoadResult.NotFound<T>(), null);
            }

            // Identity before contents: a file that is not ours should be reported as such rather
            // than as a signature failure, which would read as tampering.
            var adoption = Adoption.CanAdopt(bytes, _identity, _schemaVersion);

            switch (adoption.Verdict)
            {
                case AdoptionVerdict.Adoptable:
                    break;

                case AdoptionVerdict.NotEternalFile:
                    return (LoadResult.Failed<T>(LoadStatus.NotEternalFile, adoption.Reason), null);

                case AdoptionVerdict.ContainerTooNew:
                    return (LoadResult.Failed<T>(LoadStatus.ContainerTooNew, adoption.Reason), null);

                case AdoptionVerdict.SchemaTooNew:
                    return (LoadResult.Failed<T>(LoadStatus.SchemaTooNew, adoption.Reason), null);

                case AdoptionVerdict.NoMigrationPath:
                    return (LoadResult.Failed<T>(LoadStatus.MigrationFailed, adoption.Reason), null);

                case AdoptionVerdict.ForeignProduct:
                    return (LoadResult.Failed<T>(LoadStatus.NotEternalFile, adoption.Reason), null);

                default:
                    return (LoadResult.Failed<T>(LoadStatus.Corrupt, adoption.Reason), null);
            }

            var body = ContainerReader.ReadBody(bytes, _transforms, _keys, out var report);

            if (!body.IsOk)
            {
                return (body.CastFailure<T>(), report);
            }

            if (!ContainerReader.TryReadPreamble(bytes, out var preamble, out _))
            {
                return (LoadResult.Failed<T>(LoadStatus.Corrupt, "The preamble stopped parsing after it had verified."), report);
            }

            if (preamble.SerializerId != _serializer.FormatId)
            {
                return (LoadResult.Failed<T>(
                    LoadStatus.UnknownSerializer,
                    "The record was written with serializer 0x" + preamble.SerializerId.ToString("X2") +
                    " and this driver reads 0x" + _serializer.FormatId.ToString("X2") + ".",
                    report), report);
            }

            try
            {
                return (LoadResult.Ok((T)_serializer.Deserialize(body.Value, typeof(T))), report);
            }
            catch (Exception error)
            {
                // The signature held, so these are our bytes and we still could not read them.
                _log.Error("Deserializing " + path + " failed on a record that verified.", error);
                return (LoadResult.Failed<T>(LoadStatus.Corrupt, error.Message, report), report);
            }
        }
    }
}
