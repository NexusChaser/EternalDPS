using System;
using NUnit.Framework;

namespace NexusChaser.EternalDPS.Tests
{
    /// <summary>
    /// Covers the timing that turns a dragged slider into one write. Stepped by hand rather than by
    /// a frame loop, so the behaviour is checked exactly instead of approximately.
    /// </summary>
    public class SaveDebouncerTests
    {
        private static readonly EternalKey Volume = EternalKey.AccountSettings("prefs");
        private static readonly EternalKey Display = EternalKey.MachineSettings("display");

        [Test]
        public void Nothing_is_due_when_nothing_changed()
        {
            var debouncer = new SaveDebouncer(1f);

            Assert.IsTrue(debouncer.IsIdle);
            Assert.AreEqual(0, debouncer.Tick(10f).Count);
        }

        [Test]
        public void A_change_becomes_due_once_the_quiet_period_has_passed()
        {
            var debouncer = new SaveDebouncer(1f);

            debouncer.MarkDirty(Volume);
            Assert.IsTrue(debouncer.IsPending(Volume));

            Assert.AreEqual(0, debouncer.Tick(0.5f).Count, "Half a second is not the quiet period.");

            var due = debouncer.Tick(0.5f);

            Assert.AreEqual(1, due.Count);
            Assert.AreEqual(Volume, due[0]);
            Assert.IsTrue(debouncer.IsIdle);
        }

        [Test]
        public void A_dragged_slider_produces_exactly_one_write()
        {
            // The whole reason this exists. Fifty frames of change, one write — and the value that
            // gets written is the last one, because the value is read when the write happens.
            var debouncer = new SaveDebouncer(1f);
            var writes = 0;

            for (var frame = 0; frame < 50; frame++)
            {
                debouncer.MarkDirty(Volume);
                writes += debouncer.Tick(1f / 60f).Count;
            }

            Assert.AreEqual(0, writes, "Nothing should be written while the value is still moving.");

            writes += debouncer.Tick(1f).Count;

            Assert.AreEqual(1, writes);
        }

        [Test]
        public void The_countdown_restarts_on_every_change()
        {
            var debouncer = new SaveDebouncer(1f);

            debouncer.MarkDirty(Volume);
            debouncer.Tick(0.9f);

            debouncer.MarkDirty(Volume);
            Assert.AreEqual(0, debouncer.Tick(0.9f).Count, "The second change should have restarted the wait.");

            Assert.AreEqual(1, debouncer.Tick(0.2f).Count);
        }

        [Test]
        public void Records_are_timed_independently()
        {
            var debouncer = new SaveDebouncer(1f);

            debouncer.MarkDirty(Volume);
            debouncer.Tick(0.6f);
            debouncer.MarkDirty(Display);

            var due = debouncer.Tick(0.5f);

            Assert.AreEqual(1, due.Count);
            Assert.AreEqual(Volume, due[0]);
            Assert.IsTrue(debouncer.IsPending(Display));
        }

        [Test]
        public void A_record_marked_twice_is_written_once()
        {
            var debouncer = new SaveDebouncer(1f);

            debouncer.MarkDirty(Volume);
            debouncer.MarkDirty(Volume);

            Assert.AreEqual(1, debouncer.PendingCount);
            Assert.AreEqual(1, debouncer.Tick(1f).Count);
        }

        [Test]
        public void Draining_gives_up_waiting_and_returns_everything()
        {
            // What happens when the application is going away: there may be no more frames, so a
            // countdown that has not finished never will.
            var debouncer = new SaveDebouncer(10f);

            debouncer.MarkDirty(Volume);
            debouncer.MarkDirty(Display);

            var drained = debouncer.DrainAll();

            Assert.AreEqual(2, drained.Count);
            Assert.IsTrue(debouncer.IsIdle);
            Assert.AreEqual(0, debouncer.Tick(100f).Count);
        }

        [Test]
        public void A_zero_quiet_period_writes_on_the_next_tick()
        {
            var debouncer = new SaveDebouncer(0f);

            debouncer.MarkDirty(Volume);

            Assert.AreEqual(1, debouncer.Tick(0f).Count);
        }

        [Test]
        public void A_cancelled_change_is_never_written()
        {
            var debouncer = new SaveDebouncer(1f);

            debouncer.MarkDirty(Volume);
            debouncer.Cancel(Volume);

            Assert.AreEqual(0, debouncer.Tick(10f).Count);
        }

        [Test]
        public void A_negative_delta_does_not_move_time_backwards()
        {
            // Time going backwards would leave a record pending forever.
            var debouncer = new SaveDebouncer(1f);

            debouncer.MarkDirty(Volume);
            debouncer.Tick(-5f);

            Assert.AreEqual(1, debouncer.Tick(1f).Count);
        }

        [Test]
        public void A_negative_quiet_period_is_refused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SaveDebouncer(-1f));
        }
    }
}
