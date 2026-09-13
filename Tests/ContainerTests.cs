using System;
using System.Collections.Generic;
using System.Text;
using NexusChaser.EternalDPS.Abstractions;
using NexusChaser.EternalDPS.Container;
using NexusChaser.EternalDPS.Keys;
using NexusChaser.EternalDPS.Transforms;
using NUnit.Framework;

namespace NexusChaser.EternalDPS.Tests
{
    /// <summary>
    /// Covers the container: its layout, its signature, and the things that are supposed to be
    /// readable when the rest of it is not.
    /// </summary>
    public class ContainerTests
    {
        private static readonly Guid Product = new Guid("11111111-2222-3333-4444-555555555555");

        private static byte[] Body(string text)
        {
            return Encoding.UTF8.GetBytes(text);
        }

        private static SaveMetadata Metadata()
        {
            return new SaveMetadata
            {
                ProductId = Product,
                SchemaVersion = 3,
                SavedAtUtc = new DateTimeOffset(2026, 9, 13, 18, 30, 0, TimeSpan.Zero),
                AppVersion = "1.4.2",
                SlotName = "Partida de Fabrizio",
                Playtime = TimeSpan.FromMinutes(742),
            };
        }

        private static byte[] WriteSample(
            IKeyProvider keys,
            IReadOnlyList<IByteTransform> transforms = null,
            string text = "board state",
            SaveMetadata metadata = null)
        {
            return ContainerWriter.Write(
                Body(text),
                EternalIds.SerializerJson,
                transforms ?? new IByteTransform[] { new DeflateTransform() },
                metadata ?? Metadata(),
                keys);
        }

        [Test]
        public void A_container_starts_with_the_magic_and_declares_its_layout()
        {
            var file = WriteSample(TestKeyProvider.WithSingleKey());

            Assert.AreEqual((byte)'E', file[0]);
            Assert.AreEqual((byte)'T', file[1]);
            Assert.AreEqual((byte)'M', file[2]);
            Assert.AreEqual((byte)'1', file[3]);

            Assert.IsTrue(ContainerReader.TryReadPreamble(file, out var preamble, out _));

            Assert.AreEqual(ContainerFormat.CurrentVersion, preamble.Version);
            Assert.IsTrue(preamble.IsSigned);
            Assert.IsTrue(preamble.HasMetadata);
            Assert.AreEqual(EternalIds.SerializerJson, preamble.SerializerId);
            Assert.AreEqual(file.Length, preamble.ExpectedTotalLength);
        }

        [Test]
        public void The_key_id_sits_at_offset_thirteen_and_is_readable_before_anything_is_verified()
        {
            // It has to be: it is what says which key to verify with. Reserving the byte is also why
            // rotation can arrive later without changing the container version.
            var file = WriteSample(TestKeyProvider.WithSingleKey(id: 7));

            Assert.AreEqual(7, file[ContainerFormat.OffsetKeyId]);
            Assert.AreEqual(13, ContainerFormat.OffsetKeyId);
        }

        [Test]
        public void A_body_survives_the_round_trip_through_the_transform_chain()
        {
            var keys = TestKeyProvider.WithSingleKey();
            var file = WriteSample(keys, text: "exactly this");

            var body = ContainerReader.ReadBody(file, TransformRegistry.WithBuiltIns(), keys, out var report);

            Assert.IsTrue(report.IsValid, report.ToString());
            Assert.IsTrue(body.IsOk);
            Assert.AreEqual("exactly this", Encoding.UTF8.GetString(body.Value));
        }

        [Test]
        public void Transforms_are_undone_back_to_front()
        {
            // Two different transforms, so applying them in the wrong order produces different
            // bytes and the test would notice.
            var keys = TestKeyProvider.WithSingleKey();
            var chain = new IByteTransform[] { new DeflateTransform(), new ReversingTransform() };

            var file = ContainerWriter.Write(
                Body("order matters"), EternalIds.SerializerJson, chain, Metadata(), keys);

            var registry = TransformRegistry.WithBuiltIns();
            registry.Register(new ReversingTransform());

            var body = ContainerReader.ReadBody(file, registry, keys, out _);

            Assert.IsTrue(body.IsOk);
            Assert.AreEqual("order matters", Encoding.UTF8.GetString(body.Value));
        }

        [Test]
        public void An_empty_body_round_trips()
        {
            var keys = TestKeyProvider.WithSingleKey();
            var file = WriteSample(keys, text: string.Empty);

            var body = ContainerReader.ReadBody(file, TransformRegistry.WithBuiltIns(), keys, out _);

            Assert.IsTrue(body.IsOk);
            Assert.AreEqual(0, body.Value.Length);
        }

        [Test]
        public void A_single_flipped_bit_anywhere_is_detected()
        {
            var keys = TestKeyProvider.WithSingleKey();
            var original = WriteSample(keys);

            ContainerReader.TryReadPreamble(original, out var preamble, out _);

            var probes = new Dictionary<string, int>
            {
                { "preamble", ContainerFormat.OffsetSerializerId },
                { "metadata", preamble.MetadataOffset + 20 },
                { "body", preamble.BodyOffset },
                { "signature", preamble.SignatureOffset },
            };

            foreach (var probe in probes)
            {
                var tampered = (byte[])original.Clone();
                tampered[probe.Value] ^= 0x01;

                var report = ContainerReader.Verify(tampered, keys);

                Assert.IsFalse(report.IsValid, "A flipped bit in the " + probe.Key + " went undetected.");
            }
        }

        [Test]
        public void Stripping_a_transform_from_the_header_does_not_get_past_the_signature()
        {
            // The downgrade the architecture calls out: if the signature only covered the body,
            // deleting the encryption identifier from the header would be enough to make a reader
            // hand over a payload it should have refused, because the file would simply claim it
            // was never encrypted.
            var keys = TestKeyProvider.WithSingleKey();
            var original = WriteSample(keys);

            ContainerReader.TryReadPreamble(original, out var preamble, out _);
            Assert.AreEqual(1, preamble.TransformIds.Length, "The sample should carry one transform.");

            var downgraded = (byte[])original.Clone();
            downgraded[ContainerFormat.OffsetTransformIds] = EternalIds.TransformAes256Cbc;

            var report = ContainerReader.Verify(downgraded, keys);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(IntegrityStatus.SignatureMismatch, report.Status);
            Assert.AreEqual(ContainerRegion.Signature, report.FailedRegion);
        }

        [Test]
        public void A_file_that_is_not_ours_is_reported_as_foreign_rather_than_as_damage()
        {
            var report = ContainerReader.Verify(Encoding.UTF8.GetBytes("{\"hello\":1}"), TestKeyProvider.WithSingleKey());

            Assert.AreEqual(IntegrityStatus.MagicMismatch, report.Status);
        }

        [Test]
        public void A_truncated_file_is_told_apart_from_a_tampered_one()
        {
            // Different causes — an interrupted write versus an edit — and they need different
            // answers, so the report has to distinguish them.
            var keys = TestKeyProvider.WithSingleKey();
            var original = WriteSample(keys);

            var cut = new byte[original.Length - 10];
            Array.Copy(original, cut, cut.Length);

            var report = ContainerReader.Verify(cut, keys);

            Assert.AreEqual(IntegrityStatus.Truncated, report.Status);
            Assert.AreEqual(original.Length, report.DeclaredBodyLength + report.DeclaredMetadataLength
                                             + ContainerFormat.PreambleFixedLength + 1
                                             + ContainerFormat.SignatureLength);
            Assert.AreEqual(cut.Length, report.ActualLength);
        }

        [Test]
        public void A_container_from_a_newer_build_is_reported_as_newer_and_not_as_broken()
        {
            var keys = TestKeyProvider.WithSingleKey();
            var file = WriteSample(keys);

            file[ContainerFormat.OffsetVersion] = 99;

            var report = ContainerReader.Verify(file, keys);

            Assert.AreEqual(IntegrityStatus.UnsupportedVersion, report.Status);

            // And it has to reach the caller as "too new", never as "damaged", or a build would
            // feel entitled to overwrite it.
            var result = LoadResult.FromIntegrity<string>(report);
            Assert.AreEqual(LoadStatus.ContainerTooNew, result.Status);
        }

        [Test]
        public void Metadata_is_readable_while_the_body_is_wrecked()
        {
            // The corollary almost nobody implements: a damaged save must not vanish from the slot
            // list, or the player cannot even delete it.
            var keys = TestKeyProvider.WithSingleKey();
            var file = WriteSample(keys);

            ContainerReader.TryReadPreamble(file, out var preamble, out _);

            for (var i = preamble.BodyOffset; i < preamble.SignatureOffset; i++)
            {
                file[i] = 0xFF;
            }

            Assert.IsFalse(ContainerReader.Verify(file, keys).IsValid, "The wrecked body should fail verification.");

            var metadata = ContainerReader.ReadMetadata(file);

            Assert.IsTrue(metadata.IsOk);
            Assert.AreEqual("Partida de Fabrizio", metadata.Value.SlotName);
            Assert.AreEqual(TimeSpan.FromMinutes(742), metadata.Value.Playtime);
        }

        [Test]
        public void Metadata_survives_every_field_including_a_thumbnail()
        {
            var keys = TestKeyProvider.WithSingleKey();
            var source = Metadata();
            source.Thumbnail = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
            source.Custom["chapter"] = "The Wizard's Tower";
            source.Custom["players"] = "4";

            var file = WriteSample(keys, metadata: source);
            var read = ContainerReader.ReadMetadata(file).ValueOrThrow();

            Assert.AreEqual(source.ProductId, read.ProductId);
            Assert.AreEqual(source.SchemaVersion, read.SchemaVersion);
            Assert.AreEqual(source.SavedAtUtc, read.SavedAtUtc);
            Assert.AreEqual(source.AppVersion, read.AppVersion);
            Assert.AreEqual(source.SlotName, read.SlotName);
            Assert.AreEqual(source.Playtime, read.Playtime);
            CollectionAssert.AreEqual(source.Thumbnail, read.Thumbnail);
            Assert.AreEqual("The Wizard's Tower", read.Custom["chapter"]);
            Assert.AreEqual("4", read.Custom["players"]);
        }

        [Test]
        public void A_slot_list_can_skip_the_thumbnail()
        {
            var keys = TestKeyProvider.WithSingleKey();
            var source = Metadata();
            source.Thumbnail = new byte[512];

            var file = WriteSample(keys, metadata: source);
            var read = ContainerReader.ReadMetadata(file, includeThumbnail: false).ValueOrThrow();

            Assert.IsFalse(read.HasThumbnail);
            Assert.AreEqual(source.SlotName, read.SlotName);
        }

        [Test]
        public void Encoding_the_same_metadata_twice_produces_the_same_bytes()
        {
            // Golden-file tests depend on this, and so does any attempt to diff two saves.
            var first = Metadata();
            first.Custom["zulu"] = "last";
            first.Custom["alpha"] = "first";

            var second = Metadata();
            second.Custom["alpha"] = "first";
            second.Custom["zulu"] = "last";

            CollectionAssert.AreEqual(MetadataCodec.Encode(first), MetadataCodec.Encode(second));
        }

        [Test]
        public void A_product_id_is_written_in_rfc_4122_order()
        {
            // Not .NET's own layout, which reverses the first three fields. An external tool
            // reading this file has to see the bytes the GUID's text form implies.
            var metadata = Metadata();
            metadata.ProductId = new Guid("00112233-4455-6677-8899-aabbccddeeff");

            var block = MetadataCodec.Encode(metadata);

            Assert.AreEqual(0x00, block[1]);
            Assert.AreEqual(0x11, block[2]);
            Assert.AreEqual(0x22, block[3]);
            Assert.AreEqual(0x33, block[4]);
            Assert.AreEqual(0x44, block[5]);
            Assert.AreEqual(0x55, block[6]);
            Assert.AreEqual(0xFF, block[16]);
        }

        [Test]
        public void A_transform_this_build_does_not_have_is_named_rather_than_crashed_on()
        {
            var keys = TestKeyProvider.WithSingleKey();
            var file = ContainerWriter.Write(
                Body("payload"),
                EternalIds.SerializerJson,
                new IByteTransform[] { new ReversingTransform(0x90) },
                Metadata(),
                keys);

            // A registry that knows nothing about 0x90, as a build missing an optional module.
            var body = ContainerReader.ReadBody(file, TransformRegistry.WithBuiltIns(), keys, out _);

            Assert.AreEqual(LoadStatus.UnknownTransform, body.Status);
            Assert.That(body.Detail, Does.Contain("0x90"));
        }

        [Test]
        public void Signing_without_a_key_fails_loudly_instead_of_writing_an_unsigned_save()
        {
            var error = Assert.Throws<EternalKeyMissingException>(
                () => WriteSample(null));

            Assert.That(error.Message, Does.Contain("public repository"));
        }

        [Test]
        public void A_retired_key_may_not_sign()
        {
            var readOnly = new TestKeyProvider(
                new EternalKeyMaterial(1, TestDoubles.KeyBytes(1), KeyState.ReadOnly));

            Assert.Throws<EternalKeyMissingException>(() => WriteSample(readOnly));
        }

        [Test]
        public void A_file_written_with_a_retired_key_still_verifies()
        {
            // The point of retiring in stages: a save written before the rotation keeps loading,
            // and gets re-signed the next time it is written.
            var beforeRotation = TestKeyProvider.WithSingleKey(id: 1);
            var file = WriteSample(beforeRotation);

            var afterRotation = new TestKeyProvider(
                new EternalKeyMaterial(2, TestDoubles.KeyBytes(2)),
                new EternalKeyMaterial(1, TestDoubles.KeyBytes(1), KeyState.ReadOnly));

            Assert.IsTrue(ContainerReader.Verify(file, afterRotation).IsValid);
        }

        [Test]
        public void A_file_signed_with_a_key_this_build_lacks_says_so()
        {
            var file = WriteSample(TestKeyProvider.WithSingleKey(id: 4));

            var report = ContainerReader.Verify(file, TestKeyProvider.WithSingleKey(id: 9));

            Assert.AreEqual(IntegrityStatus.UnknownKey, report.Status);
            Assert.AreEqual(4, report.KeyId);
        }

        [Test]
        public void A_key_retired_all_the_way_is_refused()
        {
            var file = WriteSample(TestKeyProvider.WithSingleKey(id: 1));

            var rejected = new TestKeyProvider(
                new EternalKeyMaterial(2, TestDoubles.KeyBytes(2)),
                new EternalKeyMaterial(1, TestDoubles.KeyBytes(1), KeyState.Rejected));

            Assert.AreEqual(IntegrityStatus.RejectedKey, ContainerReader.Verify(file, rejected).Status);
        }

        [Test]
        public void An_unsigned_container_is_reported_as_vouching_for_nothing()
        {
            var file = ContainerWriter.WriteUnsigned(
                Body("dev build"),
                EternalIds.SerializerJson,
                new IByteTransform[] { new DeflateTransform() },
                Metadata());

            var report = ContainerReader.Verify(file, TestKeyProvider.WithSingleKey());

            Assert.AreEqual(IntegrityStatus.Unsigned, report.Status);
            Assert.IsFalse(report.IsValid);
        }

        [Test]
        public void A_key_below_the_floor_is_refused_when_it_is_built()
        {
            // The floor is the package's to impose: getting it wrong cannot be fixed after release.
            Assert.Throws<ArgumentException>(() => new EternalKeyMaterial(1, new byte[16]));
            Assert.DoesNotThrow(() => new EternalKeyMaterial(1, new byte[EternalKeyMaterial.MinimumLength]));
        }

        [Test]
        public void A_key_never_prints_its_material()
        {
            var key = new EternalKeyMaterial(3, TestDoubles.KeyBytes(200));

            Assert.That(key.ToString(), Does.Not.Contain(((byte)200).ToString()));
            Assert.That(key.ToString(), Does.Contain("key 3"));
        }

        [Test]
        public void Handing_out_key_material_hands_out_a_copy()
        {
            var key = new EternalKeyMaterial(1, TestDoubles.KeyBytes(1));

            var taken = key.CopyValue();
            Array.Clear(taken, 0, taken.Length);

            CollectionAssert.AreNotEqual(taken, key.CopyValue());
        }

        [Test]
        public void Deflate_round_trips_everything_including_nothing()
        {
            var transform = new DeflateTransform();

            var inputs = new[]
            {
                new byte[0],
                new byte[] { 42 },
                Encoding.UTF8.GetBytes(new string('a', 10000)),
            };

            foreach (var input in inputs)
            {
                CollectionAssert.AreEqual(input, transform.Invert(transform.Apply(input)));
            }

            Assert.AreEqual(EternalIds.TransformDeflate, transform.TransformId);
        }

        [Test]
        public void Deflate_actually_shrinks_something_repetitive()
        {
            var input = Encoding.UTF8.GetBytes(new string('a', 10000));

            Assert.Less(new DeflateTransform().Apply(input).Length, input.Length / 10);
        }

        [Test]
        public void A_registry_refuses_to_hand_an_identifier_to_two_implementations()
        {
            var registry = new TransformRegistry();
            registry.Register(new ReversingTransform(0x80));

            // Registering the same thing again is a no-op, not an error: startup code runs twice.
            Assert.DoesNotThrow(() => registry.Register(new ReversingTransform(0x80)));

            var error = Assert.Throws<ArgumentException>(() => registry.Register(new ClashingTransform()));
            Assert.That(error.Message, Does.Contain("never reused"));
        }

        [Test]
        public void A_registry_refuses_identifier_zero()
        {
            var registry = new TransformRegistry();

            Assert.Throws<ArgumentException>(() => registry.Register(new ReversingTransform(0x00)));
        }

        [Test]
        public void The_built_in_registry_knows_deflate_and_nothing_it_should_not()
        {
            var registry = TransformRegistry.WithBuiltIns();

            Assert.IsTrue(registry.Contains(EternalIds.TransformDeflate));
            Assert.IsFalse(registry.Contains(EternalIds.TransformAes256Cbc), "AES is a separate assembly.");
        }

        /// <summary>A second implementation that wants an identifier already taken.</summary>
        private sealed class ClashingTransform : IByteTransform
        {
            public byte TransformId => 0x80;

            public byte[] Apply(byte[] input) => input;

            public byte[] Invert(byte[] input) => input;
        }
    }
}
