using System;
using NUnit.Framework;

namespace NexusChaser.EternalDPS.Tests
{
    /// <summary>
    /// Covers the base types: slot identity, key composition and record id validation.
    /// </summary>
    public class CoreModelTests
    {
        [Test]
        public void Default_slot_is_the_default_value_of_the_struct()
        {
            // A game that never mentions slots gets this one without doing anything, which is the
            // whole point: one code path whether slots are used or not.
            Assert.AreEqual(SlotId.Default, default(SlotId));
            Assert.IsTrue(SlotId.Default.IsDefault);
        }

        [Test]
        public void New_slots_are_distinct_and_are_not_the_default_one()
        {
            var a = SlotId.New();
            var b = SlotId.New();

            Assert.AreNotEqual(a, b);
            Assert.IsFalse(a.IsDefault);
        }

        [Test]
        public void Slot_ids_survive_a_round_trip_through_text()
        {
            var original = SlotId.New();

            Assert.IsTrue(SlotId.TryParse(original.ToString(), out var parsed));
            Assert.AreEqual(original, parsed);
        }

        [Test]
        public void Default_slot_survives_a_round_trip_through_text()
        {
            Assert.IsTrue(SlotId.TryParse(SlotId.Default.ToString(), out var parsed));
            Assert.AreEqual(SlotId.Default, parsed);
        }

        [Test]
        public void Slot_segments_are_lowercase_hex_with_no_separators()
        {
            var text = SlotId.New().ToString();

            // Same segment on a case-sensitive filesystem and a case-insensitive one.
            Assert.AreEqual(32, text.Length);
            Assert.AreEqual(text.ToLowerInvariant(), text);
        }

        [Test]
        public void Paths_match_the_documented_layout()
        {
            Assert.AreEqual(
                "machine/display.etm",
                EternalKey.MachineSettings("display").RelativePath);

            Assert.AreEqual(
                "account/prefs.etm",
                EternalKey.AccountSettings("prefs").RelativePath);

            Assert.AreEqual(
                "slots/default/save.etm",
                EternalKey.SlotSession("save").RelativePath);
        }

        [Test]
        public void A_real_slot_appears_in_the_path()
        {
            var slot = SlotId.New();
            var key = EternalKey.SlotProgress("save", slot);

            Assert.AreEqual("slots/" + slot + "/save.etm", key.RelativePath);
        }

        [Test]
        public void Scopes_without_slots_collapse_the_segment_and_the_slot_itself()
        {
            // Passing a slot to an account record must not produce a key that differs from the one
            // built without it: they would compare unequal while pointing at the same file.
            var withSlot = new EternalKey(Scope.Account, RecordKind.Progress, "progress", SlotId.New());
            var withoutSlot = new EternalKey(Scope.Account, RecordKind.Progress, "progress");

            Assert.AreEqual(SlotId.Default, withSlot.Slot);
            Assert.AreEqual(withoutSlot, withSlot);
            Assert.AreEqual("account/progress.etm", withSlot.RelativePath);
        }

        [Test]
        public void Record_ids_are_lowercased()
        {
            // Windows is case-insensitive and Linux is not. Folding the case makes "MySave" and
            // "mysave" one record everywhere instead of one on Windows and two on Linux.
            var upper = EternalKey.AccountSettings("MySave");
            var lower = EternalKey.AccountSettings("mysave");

            Assert.AreEqual("mysave", upper.RecordId);
            Assert.AreEqual(lower, upper);
        }

        [TestCase("", TestName = "Record id: empty")]
        [TestCase(null, TestName = "Record id: null")]
        [TestCase("..", TestName = "Record id: parent directory")]
        [TestCase("../escape", TestName = "Record id: traversal")]
        [TestCase("sub/save", TestName = "Record id: separator")]
        [TestCase("save.etm", TestName = "Record id: dot")]
        [TestCase("save ", TestName = "Record id: trailing space")]
        [TestCase("-save", TestName = "Record id: leading separator")]
        [TestCase("con", TestName = "Record id: Windows device name")]
        [TestCase("com1", TestName = "Record id: Windows serial port")]
        [TestCase("savé", TestName = "Record id: non-ASCII")]
        public void Invalid_record_ids_are_rejected(string recordId)
        {
            Assert.IsFalse(EternalKey.IsValidRecordId(recordId));
            Assert.Throws<ArgumentException>(() => EternalKey.AccountSettings(recordId));
        }

        [TestCase("save")]
        [TestCase("auto-save")]
        [TestCase("quick_save")]
        [TestCase("slot3")]
        [TestCase("com10")]
        public void Valid_record_ids_are_accepted(string recordId)
        {
            Assert.IsTrue(EternalKey.IsValidRecordId(recordId));
        }

        [Test]
        public void Record_ids_longer_than_the_limit_are_rejected()
        {
            var tooLong = new string('a', EternalKey.MaxRecordIdLength + 1);

            Assert.IsFalse(EternalKey.IsValidRecordId(tooLong));
            Assert.IsTrue(EternalKey.IsValidRecordId(tooLong.Substring(1)));
        }

        [Test]
        public void Two_kinds_sharing_a_record_id_are_reported_as_conflicting()
        {
            // The layout has no segment for the kind, so these two land on the same file. The
            // system does not stop it, but it has to be able to name it.
            var progress = new EternalKey(Scope.Slot, RecordKind.Progress, "save");
            var session = new EternalKey(Scope.Slot, RecordKind.Session, "save");

            Assert.AreNotEqual(progress, session);
            Assert.AreEqual(progress.RelativePath, session.RelativePath);
            Assert.IsTrue(progress.ConflictsWith(session));
        }

        [Test]
        public void Different_record_ids_never_conflict()
        {
            var progress = new EternalKey(Scope.Slot, RecordKind.Progress, "progress");
            var session = new EternalKey(Scope.Slot, RecordKind.Session, "session");

            Assert.IsFalse(progress.ConflictsWith(session));
        }

        [Test]
        public void The_first_backup_is_plain_bak()
        {
            var key = EternalKey.SlotProgress("save");

            Assert.AreEqual("slots/default/save.etm.bak", key.RelativeBackupPath(1));
            Assert.AreEqual("slots/default/save.etm.bak2", key.RelativeBackupPath(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => key.RelativeBackupPath(0));
        }

        [Test]
        public void Machine_settings_never_leave_the_machine()
        {
            // Pushing this computer's resolution to the player's laptop is a bug, so the preset
            // cannot be allowed to drift into Sync unnoticed.
            Assert.AreEqual(CloudPolicy.Local, SaveProfile.MachineSettings.Cloud);
            Assert.AreEqual(CloudPolicy.Local, SaveProfile.For(Scope.Machine, RecordKind.Settings).Cloud);
        }

        [Test]
        public void Progress_recovers_and_sessions_are_discarded()
        {
            // The single difference that matters between the two, expressed as policy rather than
            // as two separate systems.
            Assert.AreEqual(IntegrityFailurePolicy.TryBackup, SaveProfile.Progress.OnIntegrityFailure);
            Assert.AreEqual(MigrationFailurePolicy.Fail, SaveProfile.Progress.OnMigrationFailure);
            Assert.Greater(SaveProfile.Progress.Backups, 0);

            Assert.AreEqual(IntegrityFailurePolicy.DiscardAndNotify, SaveProfile.Session.OnIntegrityFailure);
            Assert.AreEqual(RecordLifetime.Session, SaveProfile.Session.Lifetime);
        }

        [Test]
        public void A_profile_cannot_keep_a_negative_number_of_backups()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SaveProfile.Progress.WithBackups(-1));
        }

        [Test]
        public void With_methods_change_one_field_and_leave_the_rest_alone()
        {
            var original = SaveProfile.Progress;
            var changed = original.WithBackups(7);

            Assert.AreEqual(7, changed.Backups);
            Assert.AreEqual(original.Cloud, changed.Cloud);
            Assert.AreEqual(original.Lifetime, changed.Lifetime);
            Assert.AreEqual(original.OnIntegrityFailure, changed.OnIntegrityFailure);
            Assert.AreNotEqual(original, changed);
        }

        [Test]
        public void The_scope_matrix_has_exactly_the_six_documented_pairings()
        {
            Assert.IsTrue(ScopeRules.IsMeaningful(Scope.Machine, RecordKind.Settings));
            Assert.IsTrue(ScopeRules.IsMeaningful(Scope.Account, RecordKind.Settings));
            Assert.IsTrue(ScopeRules.IsMeaningful(Scope.Account, RecordKind.Progress));
            Assert.IsTrue(ScopeRules.IsMeaningful(Scope.Slot, RecordKind.Progress));
            Assert.IsTrue(ScopeRules.IsMeaningful(Scope.Slot, RecordKind.Session));

            Assert.IsFalse(ScopeRules.IsMeaningful(Scope.Machine, RecordKind.Progress));
            Assert.IsFalse(ScopeRules.IsMeaningful(Scope.Machine, RecordKind.Session));
            Assert.IsFalse(ScopeRules.IsMeaningful(Scope.Account, RecordKind.Session));
            Assert.IsFalse(ScopeRules.IsMeaningful(Scope.Slot, RecordKind.Settings));
        }

        [Test]
        public void The_matrix_is_advisory_and_does_not_block_anything()
        {
            // A game may have a reason the matrix did not anticipate. The system says so and gets
            // out of the way.
            Assert.DoesNotThrow(() => new EternalKey(Scope.Machine, RecordKind.Progress, "odd"));
        }
    }
}
