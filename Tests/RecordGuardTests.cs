using System;
using System.Threading.Tasks;
using NexusChaser.EternalDPS.Abstractions;
using NUnit.Framework;

namespace NexusChaser.EternalDPS.Tests
{
    /// <summary>
    /// Covers the two checks the driver runs on the records a game declares: the conflict that
    /// would silently merge two saves, and the scope pairing that is almost always a mistake.
    /// </summary>
    public class RecordGuardTests
    {
        private static readonly Guid ThisGame = new Guid("aaaaaaaa-0000-0000-0000-000000000001");

        private InMemoryStore _store;
        private RecordingLog _log;

        [SetUp]
        public void SetUp()
        {
            _store = new InMemoryStore();
            _log = new RecordingLog();
        }

        private EternalDataDriver Build(params EternalKey[] records)
        {
            return new EternalDataDriver(new EternalDataDriverOptions
            {
                Store = _store,
                Serializer = new Utf8StringSerializer(),
                Keys = TestKeyProvider.WithSingleKey(),
                Identity = new ProductIdentity(ThisGame),
                Log = _log,
                Records = records.Length == 0 ? null : records,
            });
        }

        [Test]
        public void Two_records_that_share_a_file_are_refused_at_startup()
        {
            // The path does not encode the kind, so these two are one file. Left alone, one would
            // quietly overwrite the other and it would surface as lost progress weeks later.
            var error = Assert.Throws<EternalRecordConflictException>(() => Build(
                new EternalKey(Scope.Slot, RecordKind.Progress, "save"),
                new EternalKey(Scope.Slot, RecordKind.Session, "save")));

            Assert.That(error.Message, Does.Contain("slots/default/save.etm"));
            Assert.That(error.Message, Does.Contain("Rename one of them"));
        }

        [Test]
        public void Records_that_do_not_collide_are_accepted()
        {
            Assert.DoesNotThrow(() => Build(
                EternalKey.MachineSettings("display"),
                EternalKey.AccountSettings("prefs"),
                EternalKey.AccountProgress("progress"),
                EternalKey.SlotSession("save")));
        }

        [Test]
        public async Task A_conflict_is_caught_even_when_the_records_were_never_declared()
        {
            // Declaring them up front only buys an earlier failure. The check still happens the
            // first time each record is used.
            var driver = Build();

            await driver.SaveAsync(new EternalKey(Scope.Slot, RecordKind.Progress, "save"), "progress");

            Assert.ThrowsAsync<EternalRecordConflictException>(
                () => driver.SaveAsync(new EternalKey(Scope.Slot, RecordKind.Session, "save"), "session"));
        }

        [Test]
        public async Task Using_the_same_record_repeatedly_is_not_a_conflict()
        {
            var driver = Build();
            var key = EternalKey.SlotSession("save");

            await driver.SaveAsync(key, "one");
            await driver.SaveAsync(key, "two");

            Assert.IsTrue((await driver.LoadAsync<string>(key)).IsOk);
        }

        [Test]
        public void Different_slots_are_different_records_and_never_conflict()
        {
            var first = SlotId.New();
            var second = SlotId.New();

            Assert.DoesNotThrow(() => Build(
                EternalKey.SlotProgress("save", first),
                EternalKey.SlotProgress("save", second)));
        }

        [Test]
        public void A_pairing_outside_the_matrix_is_warned_about_and_still_allowed()
        {
            // Progress in the machine scope is progress that evaporates when the player changes
            // computer. Allowed — a game may have a reason — but never in silence.
            Assert.DoesNotThrow(() => Build(new EternalKey(Scope.Machine, RecordKind.Progress, "odd")));

            Assert.IsTrue(_log.Contains(EternalLogLevel.Warning, "machine/odd.etm"));
            Assert.IsTrue(_log.Contains(EternalLogLevel.Warning, "outside the combinations"));
        }

        [Test]
        public void The_six_ordinary_pairings_produce_no_noise()
        {
            Build(
                EternalKey.MachineSettings("display"),
                EternalKey.AccountSettings("prefs"),
                EternalKey.AccountProgress("progress"),
                EternalKey.SlotProgress("chapter"),
                EternalKey.SlotSession("save"));

            Assert.IsFalse(_log.Contains(EternalLogLevel.Warning, "outside the combinations"));
        }

        [Test]
        public async Task A_questionable_record_is_warned_about_once_and_not_on_every_call()
        {
            // A warning that repeats on every save is a warning nobody reads.
            var driver = Build();
            var key = new EternalKey(Scope.Slot, RecordKind.Settings, "odd");

            await driver.SaveAsync(key, "a");
            await driver.SaveAsync(key, "b");
            await driver.LoadAsync<string>(key);

            var warnings = 0;

            foreach (var entry in _log.Entries)
            {
                if (entry.Contains("outside the combinations"))
                {
                    warnings++;
                }
            }

            Assert.AreEqual(1, warnings);
        }
    }
}
