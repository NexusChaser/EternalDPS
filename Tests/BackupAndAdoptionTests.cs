using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NexusChaser.EternalDPS.Abstractions;
using NexusChaser.EternalDPS.Container;
using NexusChaser.EternalDPS.Stores;
using NUnit.Framework;

namespace NexusChaser.EternalDPS.Tests
{
    /// <summary>
    /// Covers backup rotation and the sweep that recovers saves left behind by a rename.
    /// </summary>
    public class BackupAndAdoptionTests
    {
        private static readonly Guid ThisGame = new Guid("aaaaaaaa-0000-0000-0000-000000000001");
        private static readonly Guid OldName = new Guid("bbbbbbbb-0000-0000-0000-000000000002");
        private static readonly Guid AnotherGame = new Guid("cccccccc-0000-0000-0000-000000000003");

        private string _parent;
        private RecordingLog _log;
        private FixedClock _clock;

        [SetUp]
        public void SetUp()
        {
            _parent = Path.Combine(Path.GetTempPath(), "eternal-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_parent);

            _log = new RecordingLog();
            _clock = new FixedClock(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_parent))
                {
                    Directory.Delete(_parent, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }

        private EternalDataDriver Driver(IStore store, Guid product, params Guid[] inherited)
        {
            return new EternalDataDriver(new EternalDataDriverOptions
            {
                Store = store,
                Serializer = new Utf8StringSerializer(),
                Keys = TestKeyProvider.WithSingleKey(),
                Identity = new ProductIdentity(product, inherited),
                Clock = _clock,
                Log = _log,
            });
        }

        private FileStore StoreIn(string folderName)
        {
            return new FileStore(Path.Combine(_parent, folderName));
        }

        // --- Backup rotation ------------------------------------------------------------------

        [Test]
        public async Task Saving_keeps_the_previous_version_as_a_backup()
        {
            var store = new InMemoryStore();
            var driver = Driver(store, ThisGame);
            var key = EternalKey.SlotProgress("progress");

            await driver.SaveAsync(key, "first");
            Assert.IsNull(store.Peek(key.RelativeBackupPath(1)), "There was nothing to back up yet.");

            await driver.SaveAsync(key, "second");

            Assert.IsNotNull(store.Peek(key.RelativeBackupPath(1)));

            var fromBackup = await driver.LoadAsync<string>(
                new EternalKey(Scope.Slot, RecordKind.Progress, "progress"));
            Assert.AreEqual("second", fromBackup.Value);
        }

        [Test]
        public async Task Copies_shift_along_and_stop_at_the_configured_depth()
        {
            var store = new InMemoryStore();
            var driver = Driver(store, ThisGame);
            var key = EternalKey.SlotProgress("progress");
            var policy = SaveProfile.Progress.WithBackups(2);

            await driver.SaveAsync(key, "one", profile: policy);
            await driver.SaveAsync(key, "two", profile: policy);
            await driver.SaveAsync(key, "three", profile: policy);
            await driver.SaveAsync(key, "four", profile: policy);

            Assert.IsNotNull(store.Peek(key.RelativeBackupPath(1)));
            Assert.IsNotNull(store.Peek(key.RelativeBackupPath(2)));
            Assert.IsNull(store.Peek(key.RelativeBackupPath(3)), "Only two copies were asked for.");
        }

        [Test]
        public async Task A_profile_with_no_backups_writes_none()
        {
            var store = new InMemoryStore();
            var driver = Driver(store, ThisGame);
            var key = EternalKey.SlotSession("save");

            await driver.SaveAsync(key, "a", profile: SaveProfile.Session);
            await driver.SaveAsync(key, "b", profile: SaveProfile.Session);

            Assert.IsNull(store.Peek(key.RelativeBackupPath(1)));
        }

        [Test]
        public async Task A_corrupt_record_is_never_promoted_over_a_good_backup()
        {
            // Without this check the damaged file would be copied over the last good copy, and the
            // next save would push it down the chain until every backup was the same broken file —
            // all of them overwritten at exactly the moment they were needed.
            var store = new InMemoryStore();
            var driver = Driver(store, ThisGame);
            var key = EternalKey.SlotProgress("progress");

            await driver.SaveAsync(key, "the good one");
            await driver.SaveAsync(key, "the second one");

            var goodBackup = store.Peek(key.RelativeBackupPath(1));

            // Now the live record rots on disk.
            var live = store.Peek(key.RelativePath);
            live[live.Length - 1] ^= 0xFF;

            await driver.SaveAsync(key, "the third one");

            CollectionAssert.AreEqual(goodBackup, store.Peek(key.RelativeBackupPath(1)));
            Assert.IsTrue(_log.Contains(EternalLogLevel.Warning, "does not verify"));
        }

        [Test]
        public async Task A_damaged_record_is_recovered_from_the_backup_the_rotation_wrote()
        {
            // End to end: the rotation writes the copy, and the load policy is the one that uses it.
            var store = new InMemoryStore();
            var driver = Driver(store, ThisGame);
            var key = EternalKey.SlotProgress("progress");

            await driver.SaveAsync(key, "forty hours");
            await driver.SaveAsync(key, "forty one hours");

            var live = store.Peek(key.RelativePath);
            live[live.Length - 1] ^= 0xFF;

            var loaded = await driver.LoadAsync<string>(key, SaveProfile.Progress);

            Assert.IsTrue(loaded.IsOk, loaded.ToString());
            Assert.AreEqual("forty hours", loaded.Value);
            Assert.IsTrue(loaded.RecoveredFromBackup);
        }

        // --- Adoption sweep -------------------------------------------------------------------

        private async Task SeedAsync(string folder, Guid product, string payload, DateTimeOffset savedAt)
        {
            _clock.UtcNow = savedAt;

            var store = StoreIn(folder);
            var driver = Driver(store, product);

            await driver.SaveAsync(EternalKey.AccountProgress("progress"), payload);
            await driver.SaveAsync(EternalKey.MachineSettings("display"), payload);
        }

        [Test]
        public async Task Saves_left_behind_by_a_rename_are_found_and_copied_across()
        {
            await SeedAsync("Studio_OldName", OldName, "forty hours", _clock.UtcNow);

            var fresh = StoreIn("Studio_NewName");
            var identity = new ProductIdentity(ThisGame, OldName);

            var result = SiblingAdoption.TryAdopt(fresh, identity, 1, _log);

            Assert.AreEqual(AdoptionOutcome.Adopted, result.Outcome);
            Assert.AreEqual(2, result.AdoptedFiles.Count);

            var driver = Driver(fresh, ThisGame, OldName);
            var loaded = await driver.LoadAsync<string>(EternalKey.AccountProgress("progress"));

            Assert.IsTrue(loaded.IsOk, loaded.ToString());
            Assert.AreEqual("forty hours", loaded.Value);
        }

        [Test]
        public async Task The_originals_are_copied_and_never_moved()
        {
            // An adoption that goes wrong must not be the thing that destroys the only copy.
            await SeedAsync("Studio_OldName", OldName, "state", _clock.UtcNow);

            var oldFile = Path.Combine(_parent, "Studio_OldName", "account", "progress.etm");
            Assert.IsTrue(File.Exists(oldFile));

            SiblingAdoption.TryAdopt(StoreIn("Studio_NewName"), new ProductIdentity(ThisGame, OldName), 1, _log);

            Assert.IsTrue(File.Exists(oldFile), "The original was moved instead of copied.");
        }

        [Test]
        public async Task The_player_is_told_rather_than_rescued_in_silence()
        {
            await SeedAsync("Studio_OldName", OldName, "state", _clock.UtcNow);

            SiblingAdoption.TryAdopt(StoreIn("Studio_NewName"), new ProductIdentity(ThisGame, OldName), 1, _log);

            Assert.IsTrue(_log.Contains(EternalLogLevel.Warning, "Recovered"));
            Assert.IsTrue(_log.Contains(EternalLogLevel.Warning, "should be told"));
        }

        [Test]
        public async Task The_sweep_does_not_run_twice()
        {
            await SeedAsync("Studio_OldName", OldName, "state", _clock.UtcNow);

            var fresh = StoreIn("Studio_NewName");
            var identity = new ProductIdentity(ThisGame, OldName);

            Assert.AreEqual(AdoptionOutcome.Adopted, SiblingAdoption.TryAdopt(fresh, identity, 1, _log).Outcome);
            Assert.AreEqual(AdoptionOutcome.AlreadyDone, SiblingAdoption.TryAdopt(fresh, identity, 1, _log).Outcome);
        }

        [Test]
        public async Task A_folder_that_already_holds_saves_is_left_alone()
        {
            await SeedAsync("Studio_OldName", OldName, "old", _clock.UtcNow);
            await SeedAsync("Studio_NewName", ThisGame, "current", _clock.UtcNow.AddDays(1));

            var result = SiblingAdoption.TryAdopt(
                StoreIn("Studio_NewName"), new ProductIdentity(ThisGame, OldName), 1, _log);

            Assert.AreEqual(AdoptionOutcome.NotNeeded, result.Outcome);

            var loaded = await Driver(StoreIn("Studio_NewName"), ThisGame, OldName)
                .LoadAsync<string>(EternalKey.AccountProgress("progress"));

            Assert.AreEqual("current", loaded.Value);
        }

        [Test]
        public async Task Another_game_s_folder_is_not_touched()
        {
            await SeedAsync("SomeOtherGame", AnotherGame, "not ours", _clock.UtcNow);

            var result = SiblingAdoption.TryAdopt(
                StoreIn("Studio_NewName"), new ProductIdentity(ThisGame, OldName), 1, _log);

            Assert.AreEqual(AdoptionOutcome.NothingFound, result.Outcome);
            Assert.Greater(result.FilesExamined, 0, "It should have looked at the files and rejected them.");
        }

        [Test]
        public async Task With_several_old_folders_the_most_recently_played_one_wins()
        {
            // Renames pile up. The one the player would expect back is the last one they played.
            await SeedAsync("Studio_Name1", OldName, "ancient", _clock.UtcNow);
            await SeedAsync("Studio_Name2", OldName, "most recent", _clock.UtcNow.AddDays(30));
            await SeedAsync("Studio_Name0", OldName, "older", _clock.UtcNow.AddDays(-10));

            var fresh = StoreIn("Studio_NewName");
            var result = SiblingAdoption.TryAdopt(fresh, new ProductIdentity(ThisGame, OldName), 1, _log);

            Assert.AreEqual(AdoptionOutcome.Adopted, result.Outcome);
            Assert.That(result.SourceDirectory, Does.EndWith("Studio_Name2"));

            var loaded = await Driver(fresh, ThisGame, OldName)
                .LoadAsync<string>(EternalKey.AccountProgress("progress"));

            Assert.AreEqual("most recent", loaded.Value);
        }

        [Test]
        public void An_empty_neighbourhood_finds_nothing_and_says_so()
        {
            var result = SiblingAdoption.TryAdopt(
                StoreIn("Studio_NewName"), new ProductIdentity(ThisGame), 1, _log);

            Assert.AreEqual(AdoptionOutcome.NothingFound, result.Outcome);
            Assert.IsFalse(result.DidAdopt);
        }

        [Test]
        public async Task A_save_from_a_newer_schema_is_not_adopted()
        {
            await SeedAsync("Studio_OldName", OldName, "from the future", _clock.UtcNow);

            // Seeded at schema 1; this build claims to be older than that is not possible, so raise
            // the seed instead: re-save with a higher schema and read with a lower one.
            var higher = new EternalDataDriver(new EternalDataDriverOptions
            {
                Store = StoreIn("Studio_OldName"),
                Serializer = new Utf8StringSerializer(),
                Keys = TestKeyProvider.WithSingleKey(),
                Identity = new ProductIdentity(OldName),
                SchemaVersion = 9,
                Clock = _clock,
                Log = _log,
            });

            await higher.SaveAsync(EternalKey.AccountProgress("progress"), "schema nine");

            var file = new FileInfo(Path.Combine(_parent, "Studio_OldName", "account", "progress.etm"));
            var report = SiblingAdoption.Inspect(file, new ProductIdentity(ThisGame, OldName), 2);

            Assert.AreEqual(AdoptionVerdict.SchemaTooNew, report.Verdict);
        }

        [Test]
        public async Task A_file_that_is_not_a_container_is_ignored_without_complaint()
        {
            Directory.CreateDirectory(Path.Combine(_parent, "Junk"));
            File.WriteAllBytes(Path.Combine(_parent, "Junk", "notes.etm"), Encoding.UTF8.GetBytes("hello"));

            await SeedAsync("Studio_OldName", OldName, "real", _clock.UtcNow);

            var fresh = StoreIn("Studio_NewName");
            var result = SiblingAdoption.TryAdopt(fresh, new ProductIdentity(ThisGame, OldName), 1, _log);

            Assert.AreEqual(AdoptionOutcome.Adopted, result.Outcome);
            Assert.That(result.SourceDirectory, Does.EndWith("Studio_OldName"));
        }

        [Test]
        public async Task The_sweep_never_leaves_the_neighbourhood()
        {
            // A save two levels up must not be found. Walking the disk looking for files that might
            // be ours is slow, invasive, and opens files the player never meant this game to read.
            var grandparent = Directory.GetParent(_parent).FullName;
            var stray = Path.Combine(grandparent, "eternal-stray-" + Guid.NewGuid().ToString("N"));

            try
            {
                await SeedAsync(Path.Combine("..", Path.GetFileName(stray)), OldName, "far away", _clock.UtcNow);

                var result = SiblingAdoption.TryAdopt(
                    StoreIn("Studio_NewName"), new ProductIdentity(ThisGame, OldName), 1, _log);

                Assert.AreEqual(AdoptionOutcome.NothingFound, result.Outcome);
            }
            finally
            {
                if (Directory.Exists(stray))
                {
                    Directory.Delete(stray, recursive: true);
                }
            }
        }
    }
}
