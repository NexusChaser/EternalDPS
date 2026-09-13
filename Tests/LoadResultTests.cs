using System;
using NUnit.Framework;

namespace NexusChaser.EternalDPS.Tests
{
    /// <summary>
    /// Covers the load verdict and the integrity report: the two types the rest of the system
    /// answers with instead of throwing.
    /// </summary>
    public class LoadResultTests
    {
        [Test]
        public void A_successful_load_carries_its_value()
        {
            var result = LoadResult.Ok(42);

            Assert.IsTrue(result.IsOk);
            Assert.IsTrue(result.TryGetValue(out var value));
            Assert.AreEqual(42, value);
            Assert.IsFalse(result.RecoveredFromBackup);
        }

        [Test]
        public void A_missing_record_is_not_a_failure()
        {
            // A first run has no saves. That has to be distinguishable from a save that exists and
            // will not load, because one starts a new game and the other needs a message.
            var result = LoadResult.NotFound<string>();

            Assert.IsFalse(result.IsOk);
            Assert.IsTrue(result.IsMissing);
            Assert.IsFalse(result.TryGetValue(out _));
        }

        [Test]
        public void A_failed_load_yields_no_value()
        {
            var result = LoadResult.Failed<string>(LoadStatus.SchemaTooNew, "Written by build 7.");

            Assert.IsFalse(result.IsOk);
            Assert.IsFalse(result.IsMissing);
            Assert.IsFalse(result.TryGetValue(out var value));
            Assert.IsNull(value);
            Assert.AreEqual("fallback", result.ValueOr("fallback"));
        }

        [Test]
        public void Recovery_from_a_backup_is_recorded_so_it_can_be_announced()
        {
            // Never silently. If the player has been moved back to an earlier point, the game has
            // to be able to say so, and it can only do that if the result says so first.
            var result = LoadResult.Ok("state", recoveredFromBackup: true);

            Assert.IsTrue(result.IsOk);
            Assert.IsTrue(result.RecoveredFromBackup);
        }

        [Test]
        public void Ok_is_not_accepted_as_a_failure()
        {
            Assert.Throws<ArgumentException>(() => LoadResult.Failed<int>(LoadStatus.Ok));
        }

        [Test]
        public void Value_or_throw_only_throws_on_a_failure()
        {
            Assert.AreEqual(1, LoadResult.Ok(1).ValueOrThrow());
            Assert.Throws<InvalidOperationException>(
                () => LoadResult.Failed<int>(LoadStatus.Corrupt).ValueOrThrow());
        }

        [Test]
        public void A_failure_can_be_carried_across_a_type_boundary()
        {
            var bytes = LoadResult.Failed<byte[]>(LoadStatus.UnknownTransform, "0x02 is not registered.");
            var typed = bytes.CastFailure<string>();

            Assert.AreEqual(LoadStatus.UnknownTransform, typed.Status);
            Assert.AreEqual(bytes.Detail, typed.Detail);
        }

        [Test]
        public void A_successful_result_cannot_be_cast_to_another_type()
        {
            Assert.Throws<InvalidOperationException>(() => LoadResult.Ok(1).CastFailure<string>());
        }

        [Test]
        public void A_foreign_file_is_reported_as_foreign_rather_than_as_tampering()
        {
            // The magic not matching usually means the file simply belongs to something else.
            // Calling that an integrity failure would accuse the player of editing a file they
            // never touched.
            var report = IntegrityReport.NotEternalFile(actualLength: 128);
            var result = LoadResult.FromIntegrity<string>(report);

            Assert.AreEqual(LoadStatus.NotEternalFile, result.Status);
            Assert.AreSame(report, result.Integrity);
        }

        [Test]
        public void A_broken_signature_is_reported_as_an_integrity_failure()
        {
            var report = IntegrityReport.Failed(
                IntegrityStatus.SignatureMismatch,
                ContainerRegion.Signature,
                "Recomputed HMAC does not match.",
                containerVersion: 1,
                keyId: 1,
                declaredMetadataLength: 64,
                declaredBodyLength: 1024,
                actualLength: 1136);

            var result = LoadResult.FromIntegrity<string>(report);

            Assert.AreEqual(LoadStatus.IntegrityFailed, result.Status);
            Assert.AreEqual(ContainerRegion.Signature, result.Integrity.FailedRegion);
        }

        [Test]
        public void A_valid_report_is_not_a_failure()
        {
            var report = IntegrityReport.Valid(1, 1, 64, 1024, 1136);

            Assert.IsTrue(report.IsValid);
            Assert.AreEqual(ContainerRegion.None, report.FailedRegion);
            Assert.Throws<ArgumentException>(() => LoadResult.FromIntegrity<string>(report));
        }

        [Test]
        public void A_report_keeps_the_sizes_it_read()
        {
            // "The signature does not match" and "the file is 40 bytes short" point at different
            // causes. Keeping the numbers is what makes them distinguishable after the fact.
            var report = IntegrityReport.Failed(
                IntegrityStatus.Truncated,
                ContainerRegion.Body,
                "File ends before the declared body length.",
                containerVersion: 1,
                keyId: 1,
                declaredMetadataLength: 64,
                declaredBodyLength: 1024,
                actualLength: 1096);

            Assert.AreEqual(1024, report.DeclaredBodyLength);
            Assert.AreEqual(1096, report.ActualLength);
            Assert.AreEqual(1, report.ContainerVersion);
        }
    }
}
