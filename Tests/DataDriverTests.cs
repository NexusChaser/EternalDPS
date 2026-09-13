using System;
using System.Threading.Tasks;
using NexusChaser.EternalDPS.Abstractions;
using NexusChaser.EternalDPS.Container;
using NexusChaser.EternalDPS.Keys;
using NUnit.Framework;

namespace NexusChaser.EternalDPS.Tests
{
    /// <summary>
    /// Covers the facade: that it wires the pieces together, and that every way a load can go wrong
    /// comes back as a verdict rather than an exception.
    /// </summary>
    public class DataDriverTests
    {
        private static readonly Guid ThisGame = new Guid("aaaaaaaa-0000-0000-0000-000000000001");
        private static readonly Guid AnotherGame = new Guid("bbbbbbbb-0000-0000-0000-000000000002");

        private InMemoryStore _store;
        private RecordingLog _log;
        private FixedClock _clock;

        private EternalDataDriver Build(
            ISerializer serializer = null,
            IKeyProvider keys = null,
            ProductIdentity identity = null,
            uint schemaVersion = 1,
            bool sign = true)
        {
            return new EternalDataDriver(new EternalDataDriverOptions
            {
                Store = _store,
                Serializer = serializer ?? new Utf8StringSerializer(),
                Keys = keys ?? TestKeyProvider.WithSingleKey(),
                Identity = identity ?? new ProductIdentity(ThisGame),
                SchemaVersion = schemaVersion,
                AppVersion = "1.0.0",
                Clock = _clock,
                Log = _log,
                SignSaves = sign,
            });
        }

        [SetUp]
        public void SetUp()
        {
            _store = new InMemoryStore();
            _log = new RecordingLog();
            _clock = new FixedClock(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
        }

        [Test]
        public async Task A_value_comes_back_exactly_as_it_went_in()
        {
            var driver = Build();
            var key = EternalKey.SlotSession("save");

            await driver.SaveAsync(key, "the board, mid match");

            var loaded = await driver.LoadAsync<string>(key);

            Assert.IsTrue(loaded.IsOk, loaded.ToString());
            Assert.AreEqual("the board, mid match", loaded.Value);
            Assert.IsFalse(loaded.RecoveredFromBackup);
        }

        [Test]
        public async Task A_save_lands_at_the_path_its_key_describes()
        {
            var driver = Build();

            await driver.SaveAsync(EternalKey.MachineSettings("display"), "quality: high");

            Assert.IsNotNull(_store.Peek("machine/display.etm"));
        }

        [Test]
        public async Task Every_save_is_flushed_because_WebGL_will_not_do_it_for_us()
        {
            var driver = Build();

            await driver.SaveAsync(EternalKey.AccountSettings("prefs"), "es");

            Assert.AreEqual(1, _store.FlushCount);
        }

        [Test]
        public async Task The_driver_stamps_identity_time_and_schema_whatever_the_caller_says()
        {
            // These three are the system's to maintain. A game that sets them wrong produces a save
            // nothing can identify afterwards.
            var driver = Build(schemaVersion: 9);
            var key = EternalKey.SlotProgress("progress");

            await driver.SaveAsync(key, "state", new SaveMetadata
            {
                ProductId = AnotherGame,
                SchemaVersion = 1,
                SavedAtUtc = DateTimeOffset.MinValue,
                SlotName = "Chapter 2",
            });

            var metadata = (await driver.ReadMetadataAsync(key)).ValueOrThrow();

            Assert.AreEqual(ThisGame, metadata.ProductId);
            Assert.AreEqual(9u, metadata.SchemaVersion);
            Assert.AreEqual(_clock.UtcNow, metadata.SavedAtUtc);

            // What the game did set is left alone.
            Assert.AreEqual("Chapter 2", metadata.SlotName);
            Assert.AreEqual("1.0.0", metadata.AppVersion);
        }

        [Test]
        public async Task A_first_run_reports_nothing_there_rather_than_failing()
        {
            var loaded = await Build().LoadAsync<string>(EternalKey.SlotSession("save"));

            Assert.IsTrue(loaded.IsMissing);
            Assert.AreEqual(LoadStatus.NotFound, loaded.Status);
        }

        [Test]
        public async Task Exists_and_delete_do_what_they_say()
        {
            var driver = Build();
            var key = EternalKey.SlotSession("save");

            Assert.IsFalse(await driver.ExistsAsync(key));

            await driver.SaveAsync(key, "x");
            Assert.IsTrue(await driver.ExistsAsync(key));

            await driver.DeleteAsync(key);
            Assert.IsFalse(await driver.ExistsAsync(key));

            // Deleting again is not an error: the caller wanted it gone and it is gone.
            Assert.DoesNotThrowAsync(() => driver.DeleteAsync(key));
        }

        [Test]
        public async Task Verifying_a_record_that_is_not_there_does_not_claim_it_was_tampered_with()
        {
            var report = await Build().VerifyAsync(EternalKey.SlotSession("save"));

            Assert.IsFalse(report.IsValid);
            Assert.AreNotEqual(IntegrityStatus.SignatureMismatch, report.Status);
        }

        [Test]
        public async Task An_edited_save_is_caught_on_load()
        {
            var driver = Build();
            var key = EternalKey.SlotProgress("progress");

            await driver.SaveAsync(key, "score: 10");

            var file = _store.Peek(key.RelativePath);
            ContainerReader.TryReadPreamble(file, out var preamble, out _);
            file[preamble.BodyOffset] ^= 0xFF;

            var loaded = await driver.LoadAsync<string>(key, SaveProfile.Progress.WithBackups(0));

            Assert.AreEqual(LoadStatus.IntegrityFailed, loaded.Status);
            Assert.IsNotNull(loaded.Integrity);
            Assert.AreEqual(ContainerRegion.Signature, loaded.Integrity.FailedRegion);
        }

        [Test]
        public async Task Progress_falls_back_to_its_backup_and_says_so()
        {
            var driver = Build();
            var key = EternalKey.SlotProgress("progress");

            await driver.SaveAsync(key, "the good one");

            // Stand in for the rotation the file store will do: keep this copy as the backup, then
            // wreck the live file.
            _store.Poke(key.RelativeBackupPath(1), _store.Peek(key.RelativePath));

            var live = _store.Peek(key.RelativePath);
            live[live.Length - 1] ^= 0xFF;

            var loaded = await driver.LoadAsync<string>(key, SaveProfile.Progress);

            Assert.IsTrue(loaded.IsOk, loaded.ToString());
            Assert.AreEqual("the good one", loaded.Value);

            // Never silently. The player lost whatever happened between the two saves.
            Assert.IsTrue(loaded.RecoveredFromBackup);
            Assert.IsTrue(_log.Contains(EternalLogLevel.Warning, "recovered"));
        }

        [Test]
        public async Task A_session_is_discarded_instead_of_reaching_for_a_backup()
        {
            // The single difference that separates progress from a session, expressed as policy.
            var driver = Build();
            var key = EternalKey.SlotSession("save");

            await driver.SaveAsync(key, "mid match");
            _store.Poke(key.RelativeBackupPath(1), _store.Peek(key.RelativePath));

            var live = _store.Peek(key.RelativePath);
            live[live.Length - 1] ^= 0xFF;

            var loaded = await driver.LoadAsync<string>(key, SaveProfile.Session);

            Assert.IsFalse(loaded.IsOk);
            Assert.AreEqual(LoadStatus.IntegrityFailed, loaded.Status);
            Assert.IsFalse(loaded.RecoveredFromBackup);
        }

        [Test]
        public async Task A_save_from_a_newer_build_is_left_exactly_where_it_is()
        {
            // Overwriting it, or replacing it with an older backup, is how a player loses a save by
            // opening the game on the wrong machine.
            var driver = Build();
            var key = EternalKey.SlotProgress("progress");

            await driver.SaveAsync(key, "from the future");
            _store.Peek(key.RelativePath)[ContainerFormat.OffsetVersion] = 99;

            var before = (byte[])_store.Peek(key.RelativePath).Clone();
            var loaded = await driver.LoadAsync<string>(key, SaveProfile.Progress);

            Assert.AreEqual(LoadStatus.ContainerTooNew, loaded.Status);
            CollectionAssert.AreEqual(before, _store.Peek(key.RelativePath));
        }

        [Test]
        public async Task A_save_with_a_newer_schema_is_refused_without_being_touched()
        {
            var key = EternalKey.SlotProgress("progress");

            await Build(schemaVersion: 5).SaveAsync(key, "written by a later build");

            var loaded = await Build(schemaVersion: 2).LoadAsync<string>(key);

            Assert.AreEqual(LoadStatus.SchemaTooNew, loaded.Status);
        }

        [Test]
        public async Task Another_game_s_save_is_not_claimed()
        {
            var key = EternalKey.SlotProgress("progress");

            await Build(identity: new ProductIdentity(AnotherGame)).SaveAsync(key, "not ours");

            var loaded = await Build(identity: new ProductIdentity(ThisGame)).LoadAsync<string>(key);

            Assert.AreEqual(LoadStatus.NotEternalFile, loaded.Status);
            Assert.That(loaded.Detail, Does.Contain(AnotherGame.ToString()));
        }

        [Test]
        public async Task A_save_written_under_the_old_name_is_still_ours()
        {
            // The rename that happens at the end of nearly every project. The identity is in the
            // file, so the folder it sits in stops mattering.
            var key = EternalKey.SlotProgress("progress");

            await Build(identity: new ProductIdentity(AnotherGame)).SaveAsync(key, "forty hours");

            var afterRename = Build(identity: new ProductIdentity(ThisGame, AnotherGame));
            var loaded = await afterRename.LoadAsync<string>(key);

            Assert.IsTrue(loaded.IsOk, loaded.ToString());
            Assert.AreEqual("forty hours", loaded.Value);
        }

        [Test]
        public async Task Adoption_answers_without_ever_deserializing_the_body()
        {
            var key = EternalKey.SlotProgress("progress");
            await Build().SaveAsync(key, "payload");

            var bytes = _store.Peek(key.RelativePath);

            var ours = Adoption.CanAdopt(bytes, new ProductIdentity(ThisGame), 1);
            Assert.IsTrue(ours.IsAdoptable);
            Assert.AreEqual(ThisGame, ours.ProductId);
            Assert.AreEqual(_clock.UtcNow, ours.SavedAtUtc);

            var theirs = Adoption.CanAdopt(bytes, new ProductIdentity(AnotherGame), 1);
            Assert.AreEqual(AdoptionVerdict.ForeignProduct, theirs.Verdict);
            Assert.That(theirs.Reason, Does.Contain(ThisGame.ToString()));
        }

        [Test]
        public void Adoption_rejects_a_file_that_is_not_a_container_on_the_first_four_bytes()
        {
            var report = Adoption.CanAdopt(new byte[] { 1, 2, 3, 4, 5 }, new ProductIdentity(ThisGame), 1);

            Assert.AreEqual(AdoptionVerdict.NotEternalFile, report.Verdict);
        }

        [Test]
        public async Task Adoption_refuses_when_no_migration_leads_forward()
        {
            var key = EternalKey.SlotProgress("progress");
            await Build(schemaVersion: 1).SaveAsync(key, "old");

            var report = Adoption.CanAdopt(
                _store.Peek(key.RelativePath),
                new ProductIdentity(ThisGame),
                currentSchemaVersion: 4,
                hasMigrationPath: from => false);

            Assert.AreEqual(AdoptionVerdict.NoMigrationPath, report.Verdict);
            Assert.That(report.Reason, Does.Contain("schema 1"));
        }

        [Test]
        public async Task A_body_written_by_another_serializer_is_named_not_guessed_at()
        {
            var key = EternalKey.SlotProgress("progress");

            await Build(serializer: new Utf8StringSerializer(0x80)).SaveAsync(key, "written as 0x80");

            var loaded = await Build(serializer: new Utf8StringSerializer(0x82)).LoadAsync<string>(key);

            Assert.AreEqual(LoadStatus.UnknownSerializer, loaded.Status);
            Assert.That(loaded.Detail, Does.Contain("0x80"));
        }

        [Test]
        public async Task A_body_that_verified_and_still_will_not_parse_is_our_bug_and_is_logged_as_one()
        {
            var key = EternalKey.SlotProgress("progress");
            var driver = Build(serializer: new ThrowingSerializer());

            await driver.SaveAsync(key, "whatever");

            var loaded = await driver.LoadAsync<string>(key, SaveProfile.Progress.WithBackups(0));

            Assert.AreEqual(LoadStatus.Corrupt, loaded.Status);
            Assert.IsTrue(_log.Contains(EternalLogLevel.Error, "verified"));
        }

        [Test]
        public void A_driver_that_signs_cannot_be_built_without_a_key()
        {
            var error = Assert.Throws<ArgumentNullException>(() => new EternalDataDriver(new EternalDataDriverOptions
            {
                Store = _store,
                Serializer = new Utf8StringSerializer(),
                Identity = new ProductIdentity(ThisGame),
            }));

            Assert.That(error.Message, Does.Contain("key provider"));
        }

        [Test]
        public async Task An_unsigned_build_says_so_on_every_single_save()
        {
            var driver = Build(keys: null, sign: false);

            await driver.SaveAsync(EternalKey.SlotSession("save"), "dev");

            Assert.IsTrue(_log.Contains(EternalLogLevel.Warning, "UNSIGNED"));
        }

        [Test]
        public void A_driver_needs_a_store_a_serializer_and_an_identity()
        {
            Assert.Throws<ArgumentNullException>(() => new EternalDataDriver(new EternalDataDriverOptions
            {
                Serializer = new Utf8StringSerializer(),
                Identity = new ProductIdentity(ThisGame),
                Keys = TestKeyProvider.WithSingleKey(),
            }));

            Assert.Throws<ArgumentNullException>(() => new EternalDataDriver(new EternalDataDriverOptions
            {
                Store = _store,
                Identity = new ProductIdentity(ThisGame),
                Keys = TestKeyProvider.WithSingleKey(),
            }));

            Assert.Throws<ArgumentNullException>(() => new EternalDataDriver(new EternalDataDriverOptions
            {
                Store = _store,
                Serializer = new Utf8StringSerializer(),
                Keys = TestKeyProvider.WithSingleKey(),
            }));
        }

        [Test]
        public void The_empty_guid_is_not_a_product_identity()
        {
            Assert.Throws<ArgumentException>(() => new ProductIdentity(Guid.Empty));
        }

        [Test]
        public async Task A_saved_record_is_binary_and_not_readable_as_text()
        {
            // The floor the architecture asks for: renaming the extension gets a curious player
            // nowhere near the contents.
            var driver = Build();
            var key = EternalKey.SlotSession("save");

            await driver.SaveAsync(key, "score=999 gold=12345 unlocked=everything");

            var text = System.Text.Encoding.UTF8.GetString(_store.Peek(key.RelativePath));

            Assert.That(text, Does.Not.Contain("score"));
            Assert.That(text, Does.Not.Contain("999"));
            Assert.That(text, Does.Not.Contain("unlocked"));
        }
    }
}
