using System;
using System.Threading.Tasks;
using NexusChaser.EternalDPS.Abstractions;
using NexusChaser.EternalDPS.Container;
using NexusChaser.EternalDPS.Keys;
using NUnit.Framework;

namespace NexusChaser.EternalDPS.Tests
{
    /// <summary>
    /// Covers key derivation, the key ring, and what happens to a save when the key it was signed
    /// with is retired.
    /// </summary>
    public class KeyRotationTests
    {
        private static readonly Guid ThisGame = new Guid("aaaaaaaa-0000-0000-0000-000000000001");

        private static byte[] Hex(string text)
        {
            var clean = text.Replace(" ", string.Empty).Replace("\n", string.Empty).Replace("\r", string.Empty);
            var bytes = new byte[clean.Length / 2];

            for (var i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(clean.Substring(i * 2, 2), 16);
            }

            return bytes;
        }

        // RFC 5869, Appendix A. A hand-written cryptographic primitive that nobody checked against
        // the specification is worse than none, because it looks like it works.

        [Test]
        public void Hkdf_matches_rfc_5869_test_case_1()
        {
            var ikm = Hex("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
            var salt = Hex("000102030405060708090a0b0c");
            var info = Hex("f0f1f2f3f4f5f6f7f8f9");

            var prk = Hkdf.Extract(salt, ikm);
            CollectionAssert.AreEqual(
                Hex("077709362c2e32df0ddc3f0dc47bba6390b6c73bb50f9c3122ec844ad7c2b3e5"), prk);

            CollectionAssert.AreEqual(
                Hex("3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865"),
                Hkdf.Expand(prk, info, 42));
        }

        [Test]
        public void Hkdf_matches_rfc_5869_test_case_2_with_longer_inputs()
        {
            var ikm = Hex(
                "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f" +
                "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f" +
                "404142434445464748494a4b4c4d4e4f");
            var salt = Hex(
                "606162636465666768696a6b6c6d6e6f707172737475767778797a7b7c7d7e7f" +
                "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f" +
                "a0a1a2a3a4a5a6a7a8a9aaabacadaeaf");
            var info = Hex(
                "b0b1b2b3b4b5b6b7b8b9babbbcbdbebfc0c1c2c3c4c5c6c7c8c9cacbcccdcecf" +
                "d0d1d2d3d4d5d6d7d8d9dadbdcdddedfe0e1e2e3e4e5e6e7e8e9eaebecedeeef" +
                "f0f1f2f3f4f5f6f7f8f9fafbfcfdfeff");

            var prk = Hkdf.Extract(salt, ikm);
            CollectionAssert.AreEqual(
                Hex("06a6b88c5853361a06104c9ceb35b45cef760014904671014a193f40c15fc244"), prk);

            // 82 bytes needs three blocks, which is what exercises the chaining of T(n-1).
            CollectionAssert.AreEqual(
                Hex("b11e398dc80327a1c8e7f78c596a49344f012eda2d4efad8a050cc4c19afa97c" +
                    "59045a99cac7827271cb41c65e590e09da3275600c2f09b8367793a9aca3db71" +
                    "cc30c58179ec3e87c14c01d5c1f3434f1d87"),
                Hkdf.Expand(prk, info, 82));
        }

        [Test]
        public void Hkdf_matches_rfc_5869_test_case_3_with_no_salt_and_no_info()
        {
            // An absent salt is not an error: the RFC says it defaults to a string of zeros the
            // length of the hash, and getting that wrong produces keys that look fine and are wrong.
            var ikm = Hex("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");

            var prk = Hkdf.Extract(null, ikm);
            CollectionAssert.AreEqual(
                Hex("19ef24a32c717b167f33a91d6f648bdf96596776afdb6377ac434c1c293ccb04"), prk);

            CollectionAssert.AreEqual(
                Hex("8da4e775a563c18f715f802a063c5a31b8a11f5c5ee1879ec3454e5f3c738d2d9d201395faa4b61a96c8"),
                Hkdf.Expand(prk, info: null, length: 42));

            // An empty salt array has to mean the same thing as no salt at all.
            CollectionAssert.AreEqual(prk, Hkdf.Extract(new byte[0], ikm));
        }

        [Test]
        public void Hkdf_refuses_to_produce_more_than_the_construction_allows()
        {
            var prk = Hkdf.Extract(null, Hex("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b"));

            Assert.Throws<ArgumentOutOfRangeException>(() => Hkdf.Expand(prk, null, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Hkdf.Expand(prk, null, Hkdf.MaxOutputLength + 1));
            Assert.DoesNotThrow(() => Hkdf.Expand(prk, null, Hkdf.MaxOutputLength));
        }

        [Test]
        public void The_same_material_derives_the_same_key_every_time()
        {
            var material = TestDoubles.KeyBytes(50);

            var first = EternalKeyDerivation.DeriveKey(1, material);
            var second = EternalKeyDerivation.DeriveKey(1, material);

            CollectionAssert.AreEqual(first.CopyValue(), second.CopyValue());
        }

        [Test]
        public void Rotating_the_identifier_produces_an_unrelated_key_from_the_same_material()
        {
            // Otherwise "rotating" would hand out the same bytes under a new number and change
            // nothing at all about a leak.
            var material = TestDoubles.KeyBytes(50);

            CollectionAssert.AreNotEqual(
                EternalKeyDerivation.DeriveKey(1, material).CopyValue(),
                EternalKeyDerivation.DeriveKey(2, material).CopyValue());
        }

        [Test]
        public void A_purpose_derives_an_unrelated_subkey()
        {
            var material = TestDoubles.KeyBytes(50);

            CollectionAssert.AreNotEqual(
                EternalKeyDerivation.DeriveKey(1, material, purpose: "settings").CopyValue(),
                EternalKeyDerivation.DeriveKey(1, material, purpose: "progress").CopyValue());
        }

        [Test]
        public void A_salt_changes_the_derived_key()
        {
            var material = TestDoubles.KeyBytes(50);

            CollectionAssert.AreNotEqual(
                EternalKeyDerivation.DeriveKey(1, material).CopyValue(),
                EternalKeyDerivation.DeriveKey(1, material, salt: new byte[] { 9, 9, 9 }).CopyValue());
        }

        [Test]
        public void Derivation_refuses_material_with_nothing_in_it()
        {
            Assert.Throws<ArgumentException>(() => EternalKeyDerivation.DeriveKey(1, new byte[8]));
            Assert.Throws<ArgumentException>(() => EternalKeyDerivation.DeriveKey(1, null));
            Assert.Throws<ArgumentException>(() => EternalKeyDerivation.DeriveKey(EternalIds.None, TestDoubles.KeyBytes(1)));
        }

        [Test]
        public void A_derived_key_is_long_enough_to_be_one()
        {
            Assert.AreEqual(
                EternalKeyMaterial.MinimumLength,
                EternalKeyDerivation.DeriveKey(1, TestDoubles.KeyBytes(50)).Length);
        }

        [Test]
        public void A_ring_signs_with_its_active_key_and_reads_with_the_rest()
        {
            var ring = new EternalKeyRing(
                new EternalKeyMaterial(3, TestDoubles.KeyBytes(3)),
                new EternalKeyMaterial(2, TestDoubles.KeyBytes(2), KeyState.Warn),
                new EternalKeyMaterial(1, TestDoubles.KeyBytes(1), KeyState.ReadOnly));

            Assert.AreEqual(3, ring.SigningKeyId);
            Assert.AreEqual(3, ring.Count);
            Assert.AreEqual(3, ring.GetSigningKey().Id);

            Assert.IsTrue(ring.TryGetKey(1, out var retired));
            Assert.AreEqual(KeyState.ReadOnly, retired.State);
            Assert.IsFalse(ring.TryGetKey(9, out _));
        }

        [Test]
        public void A_ring_refuses_two_keys_with_the_same_identifier()
        {
            // The same rule as every other identifier in the format. Two keys sharing a number
            // would make a file's keyId ambiguous.
            var error = Assert.Throws<ArgumentException>(() => new EternalKeyRing(
                new EternalKeyMaterial(1, TestDoubles.KeyBytes(1)),
                new EternalKeyMaterial(1, TestDoubles.KeyBytes(2), KeyState.ReadOnly)));

            Assert.That(error.Message, Does.Contain("never reused"));
        }

        [Test]
        public void A_ring_refuses_two_active_keys()
        {
            // Which one signs would then depend on the order they were passed in, which is exactly
            // the kind of thing that works on one machine and not another.
            Assert.Throws<EternalKeyMissingException>(() => new EternalKeyRing(
                new EternalKeyMaterial(1, TestDoubles.KeyBytes(1)),
                new EternalKeyMaterial(2, TestDoubles.KeyBytes(2))));
        }

        [Test]
        public void A_ring_refuses_to_have_no_active_key_at_all()
        {
            Assert.Throws<EternalKeyMissingException>(() => new EternalKeyRing(
                new EternalKeyMaterial(1, TestDoubles.KeyBytes(1), KeyState.ReadOnly)));

            Assert.Throws<ArgumentException>(() => new EternalKeyRing());
        }

        [Test]
        public void Rotation_only_ever_moves_to_a_higher_identifier()
        {
            Assert.Throws<EternalKeyMissingException>(() => new EternalKeyRing(
                new EternalKeyMaterial(1, TestDoubles.KeyBytes(1)),
                new EternalKeyMaterial(5, TestDoubles.KeyBytes(5), KeyState.ReadOnly)));
        }

        [Test]
        public void A_ring_hands_out_the_next_identifier_and_says_when_there_are_none_left()
        {
            var ring = new EternalKeyRing(new EternalKeyMaterial(7, TestDoubles.KeyBytes(7)));
            Assert.AreEqual(8, ring.NextFreeKeyId());

            // One byte, so 255 rotations. Saying so beats wrapping round to 1 and verifying old
            // files against the wrong key.
            var exhausted = new EternalKeyRing(new EternalKeyMaterial(255, TestDoubles.KeyBytes(255)));
            Assert.Throws<EternalKeyMissingException>(() => exhausted.NextFreeKeyId());
        }

        [Test]
        public void Re_signing_moves_a_save_onto_the_current_key_without_touching_its_contents()
        {
            var before = new EternalKeyRing(new EternalKeyMaterial(1, TestDoubles.KeyBytes(1)));

            var metadata = new SaveMetadata { SlotName = "Chapter 4", SchemaVersion = 2 };
            var file = ContainerWriter.Write(
                new byte[] { 1, 2, 3, 4 }, EternalIds.SerializerJson, null, metadata, before);

            var after = new EternalKeyRing(
                new EternalKeyMaterial(2, TestDoubles.KeyBytes(2)),
                new EternalKeyMaterial(1, TestDoubles.KeyBytes(1), KeyState.ReadOnly));

            Assert.IsTrue(ContainerWriter.TryResign(file, after, out var resigned, out _));

            Assert.AreEqual(2, resigned[ContainerFormat.OffsetKeyId]);
            Assert.IsTrue(ContainerReader.Verify(resigned, after).IsValid);
            Assert.AreEqual(file.Length, resigned.Length);

            // Everything except the key byte and the signature is untouched.
            ContainerReader.TryReadPreamble(resigned, out var preamble, out _);

            for (var i = 0; i < preamble.SignatureOffset; i++)
            {
                if (i == ContainerFormat.OffsetKeyId)
                {
                    continue;
                }

                Assert.AreEqual(file[i], resigned[i], "Byte " + i + " changed and should not have.");
            }
        }

        [Test]
        public void Re_signing_a_file_already_on_the_current_key_does_nothing()
        {
            var ring = new EternalKeyRing(new EternalKeyMaterial(1, TestDoubles.KeyBytes(1)));
            var file = ContainerWriter.Write(new byte[] { 1 }, EternalIds.SerializerJson, null, new SaveMetadata(), ring);

            Assert.IsFalse(ContainerWriter.TryResign(file, ring, out var resigned, out var report));
            Assert.IsNull(resigned);
            Assert.IsTrue(report.IsValid);
        }

        [Test]
        public void Re_signing_refuses_a_file_that_does_not_verify()
        {
            // Otherwise re-signing would take a save somebody had edited and hand it back correctly
            // signed with the current key — laundering exactly what the signature is for.
            var before = new EternalKeyRing(new EternalKeyMaterial(1, TestDoubles.KeyBytes(1)));
            var file = ContainerWriter.Write(new byte[] { 1, 2, 3 }, EternalIds.SerializerJson, null, new SaveMetadata(), before);

            ContainerReader.TryReadPreamble(file, out var preamble, out _);
            file[preamble.BodyOffset] ^= 0xFF;

            var after = new EternalKeyRing(
                new EternalKeyMaterial(2, TestDoubles.KeyBytes(2)),
                new EternalKeyMaterial(1, TestDoubles.KeyBytes(1), KeyState.ReadOnly));

            Assert.IsFalse(ContainerWriter.TryResign(file, after, out var resigned, out var report));
            Assert.IsNull(resigned);
            Assert.AreEqual(IntegrityStatus.SignatureMismatch, report.Status);
        }

        [Test]
        public void A_save_signed_with_a_retiring_key_is_reported_as_such()
        {
            var before = new EternalKeyRing(new EternalKeyMaterial(1, TestDoubles.KeyBytes(1)));
            var file = ContainerWriter.Write(new byte[] { 1 }, EternalIds.SerializerJson, null, new SaveMetadata(), before);

            var after = new EternalKeyRing(
                new EternalKeyMaterial(2, TestDoubles.KeyBytes(2)),
                new EternalKeyMaterial(1, TestDoubles.KeyBytes(1), KeyState.Warn));

            var report = ContainerReader.Verify(file, after);

            // It verifies — that is the point of retiring in stages — and it says the key is going.
            Assert.IsTrue(report.IsValid);
            Assert.IsTrue(report.SignedWithRetiringKey);
            Assert.AreEqual(KeyState.Warn, report.SigningKeyState);
        }

        [Test]
        public async Task A_save_read_after_a_rotation_is_moved_onto_the_new_key_by_itself()
        {
            var store = new InMemoryStore();
            var log = new RecordingLog();
            var key = EternalKey.AccountProgress("progress");

            var before = new EternalKeyRing(new EternalKeyMaterial(1, TestDoubles.KeyBytes(1)));
            await Driver(store, log, before).SaveAsync(key, "forty hours");

            Assert.AreEqual(1, store.Peek(key.RelativePath)[ContainerFormat.OffsetKeyId]);

            var after = new EternalKeyRing(
                new EternalKeyMaterial(2, TestDoubles.KeyBytes(2)),
                new EternalKeyMaterial(1, TestDoubles.KeyBytes(1), KeyState.Warn));

            var loaded = await Driver(store, log, after).LoadAsync<string>(key);

            Assert.IsTrue(loaded.IsOk, loaded.ToString());
            Assert.AreEqual("forty hours", loaded.Value);

            // Silently, from the player's point of view: they did nothing and see nothing.
            Assert.AreEqual(2, store.Peek(key.RelativePath)[ContainerFormat.OffsetKeyId]);
            Assert.IsTrue(log.Contains(EternalLogLevel.Warning, "being retired"));
            Assert.IsTrue(log.Contains(EternalLogLevel.Info, "Re-signed"));
        }

        [Test]
        public async Task Turning_off_automatic_re_signing_leaves_it_to_the_next_save()
        {
            var store = new InMemoryStore();
            var log = new RecordingLog();
            var key = EternalKey.AccountProgress("progress");

            var before = new EternalKeyRing(new EternalKeyMaterial(1, TestDoubles.KeyBytes(1)));
            await Driver(store, log, before).SaveAsync(key, "state");

            var after = new EternalKeyRing(
                new EternalKeyMaterial(2, TestDoubles.KeyBytes(2)),
                new EternalKeyMaterial(1, TestDoubles.KeyBytes(1), KeyState.ReadOnly));

            var driver = Driver(store, log, after, autoResign: false);

            Assert.IsTrue((await driver.LoadAsync<string>(key)).IsOk);
            Assert.AreEqual(1, store.Peek(key.RelativePath)[ContainerFormat.OffsetKeyId]);

            // Every save uses the current key, so an ordinary save does the migration anyway.
            await driver.SaveAsync(key, "state");
            Assert.AreEqual(2, store.Peek(key.RelativePath)[ContainerFormat.OffsetKeyId]);
        }

        [Test]
        public async Task A_save_signed_with_a_key_retired_all_the_way_stops_loading()
        {
            var store = new InMemoryStore();
            var log = new RecordingLog();
            var key = EternalKey.AccountProgress("progress");

            var before = new EternalKeyRing(new EternalKeyMaterial(1, TestDoubles.KeyBytes(1)));
            await Driver(store, log, before).SaveAsync(key, "state");

            var after = new EternalKeyRing(
                new EternalKeyMaterial(2, TestDoubles.KeyBytes(2)),
                new EternalKeyMaterial(1, TestDoubles.KeyBytes(1), KeyState.Rejected));

            var loaded = await Driver(store, log, after).LoadAsync<string>(key, SaveProfile.Progress.WithBackups(0));

            Assert.AreEqual(LoadStatus.IntegrityFailed, loaded.Status);
            Assert.AreEqual(IntegrityStatus.RejectedKey, loaded.Integrity.Status);
        }

        private static EternalDataDriver Driver(
            InMemoryStore store, RecordingLog log, IKeyProvider keys, bool autoResign = true)
        {
            return new EternalDataDriver(new EternalDataDriverOptions
            {
                Store = store,
                Serializer = new Utf8StringSerializer(),
                Keys = keys,
                Identity = new ProductIdentity(ThisGame),
                Log = log,
                AutoResign = autoResign,
            });
        }
    }
}
