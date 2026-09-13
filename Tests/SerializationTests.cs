#if ETERNAL_NEWTONSOFT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using NexusChaser.EternalDPS.Abstractions;
using NexusChaser.EternalDPS.Serialization;
using NUnit.Framework;

namespace NexusChaser.EternalDPS.Tests
{
    /// <summary>
    /// Covers the JSON adapter: that it round-trips, that its settings are the safe ones, and that
    /// polymorphism goes through a discriminator the game controls rather than through type names.
    /// </summary>
    public class SerializationTests
    {
        private NewtonsoftJsonSerializer _serializer;

        [SetUp]
        public void SetUp()
        {
            _serializer = new NewtonsoftJsonSerializer();
        }

        private sealed class Progress
        {
            public int Level { get; set; }
            public float Volume { get; set; }
            public double Precise { get; set; }
            public string Name { get; set; }
            public DateTimeOffset LastPlayed { get; set; }
            public DateTime Stamped { get; set; }
            public List<string> Unlocked { get; set; }
        }

        [Test]
        public void The_adapter_claims_the_registered_json_identifier()
        {
            Assert.AreEqual(EternalIds.SerializerJson, _serializer.FormatId);
            Assert.AreEqual(0x01, _serializer.FormatId);
        }

        [Test]
        public void An_object_comes_back_as_it_went_in()
        {
            var original = new Progress
            {
                Level = 34,
                Volume = 0.75f,
                Precise = 1.0 / 3.0,
                Name = "Fabrizio",
                LastPlayed = new DateTimeOffset(2026, 9, 13, 18, 30, 0, TimeSpan.Zero),
                Unlocked = new List<string> { "wizard", "chicken" },
            };

            var read = _serializer.Deserialize<Progress>(_serializer.Serialize(original));

            Assert.AreEqual(original.Level, read.Level);
            Assert.AreEqual(original.Volume, read.Volume);
            Assert.AreEqual(original.Precise, read.Precise);
            Assert.AreEqual(original.Name, read.Name);
            Assert.AreEqual(original.LastPlayed, read.LastPlayed);
            CollectionAssert.AreEqual(original.Unlocked, read.Unlocked);
        }

        [Test]
        public void Type_name_handling_cannot_be_turned_back_on()
        {
            // The known deserialization hole: with it on, the file names the .NET type to construct,
            // so anybody who can edit a save can ask the game to build a type of their choosing.
            // A project must not be able to reopen it for everything that reads its saves.
            var settings = NewtonsoftJsonSerializer.CreateSettings(s =>
            {
                s.TypeNameHandling = TypeNameHandling.All;
                s.MetadataPropertyHandling = MetadataPropertyHandling.Default;
            });

            Assert.AreEqual(TypeNameHandling.None, settings.TypeNameHandling);
            Assert.AreEqual(MetadataPropertyHandling.Ignore, settings.MetadataPropertyHandling);
        }

        [Test]
        public void A_type_name_smuggled_into_a_file_is_not_acted_on()
        {
            // The attack, not just the setting: a save that carries "$type" must be treated as data.
            var hostile = Encoding.UTF8.GetBytes(
                "{\"$type\":\"System.Diagnostics.Process, System\",\"Level\":1}");

            var read = _serializer.Deserialize<Progress>(hostile);

            Assert.IsInstanceOf<Progress>(read);
            Assert.AreEqual(1, read.Level);
        }

        [Test]
        public void Numbers_are_written_the_same_way_in_every_culture()
        {
            // The classic bug that only shows up once the game leaves the country it was built in:
            // a machine with a comma for a decimal separator writes 1,5 and nothing else can read it.
            var previous = Thread.CurrentThread.CurrentCulture;

            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("es-ES");

                var json = Encoding.UTF8.GetString(_serializer.Serialize(new Progress { Volume = 1.5f }));

                Assert.That(json, Does.Contain("1.5"));
                Assert.That(json, Does.Not.Contain("1,5"));

                Assert.AreEqual(1.5f, _serializer.Deserialize<Progress>(Encoding.UTF8.GetBytes(json)).Volume);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void A_bare_local_timestamp_is_normalised_to_utc()
        {
            // DateTime is the ambiguous one: it carries no offset, so the same text means a
            // different instant depending on where it is read. The timestamp decides which of two
            // copies is newer, and deciding that wrongly costs the player the newer one.
            var local = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Local);
            var json = Encoding.UTF8.GetString(_serializer.Serialize(new Progress { Stamped = local }));

            Assert.That(json, Does.Contain("Z\"").Or.Contain("+00:00"));

            var read = _serializer.Deserialize<Progress>(Encoding.UTF8.GetBytes(json));
            Assert.AreEqual(local.ToUniversalTime(), read.Stamped.ToUniversalTime());
        }

        [Test]
        public void An_offset_timestamp_keeps_its_offset_and_still_means_the_same_instant()
        {
            // DateTimeOffset already carries its offset, so it is not ambiguous and is left alone.
            // The consequence worth knowing: two saves written in different zones show different
            // text for the same moment, so they are compared as instants and never as strings.
            var local = new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.FromHours(-5));
            var json = Encoding.UTF8.GetString(_serializer.Serialize(new Progress { LastPlayed = local }));

            Assert.That(json, Does.Contain("2026-09-13T12:00:00-05:00"));

            var read = _serializer.Deserialize<Progress>(Encoding.UTF8.GetBytes(json));
            Assert.AreEqual(local.ToUniversalTime(), read.LastPlayed.ToUniversalTime());
            Assert.AreEqual(local, read.LastPlayed);
        }

        [Test]
        public void Nothing_is_written_with_a_byte_order_mark()
        {
            // Four bytes of noise that every other reader of this format would have to know to skip.
            var bytes = _serializer.Serialize(new Progress { Level = 1 });

            Assert.AreNotEqual(0xEF, bytes[0]);
        }

        [Test]
        public void A_byte_order_mark_that_arrived_anyway_is_tolerated()
        {
            // A save that has been through a text editor may have picked one up. Refusing it would
            // turn a cosmetic problem into a lost save.
            var withMark = new List<byte> { 0xEF, 0xBB, 0xBF };
            withMark.AddRange(Encoding.UTF8.GetBytes("{\"Level\":7}"));

            Assert.AreEqual(7, _serializer.Deserialize<Progress>(withMark.ToArray()).Level);
        }

        [Test]
        public void A_member_the_build_no_longer_knows_is_ignored_rather_than_fatal()
        {
            // Old saves carry fields that were removed. Refusing them would make every cleanup a
            // breaking change.
            var json = Encoding.UTF8.GetBytes("{\"Level\":3,\"RemovedLastYear\":\"whatever\"}");

            Assert.AreEqual(3, _serializer.Deserialize<Progress>(json).Level);
        }

        [Test]
        public void An_empty_body_deserializes_to_nothing_rather_than_throwing()
        {
            Assert.IsNull(_serializer.Deserialize<Progress>(new byte[0]));
            Assert.AreEqual(0, _serializer.Deserialize<int>(new byte[0]));
        }

        [Test]
        public void The_body_can_be_read_as_an_editable_tree()
        {
            var bytes = _serializer.Serialize(new Progress { Level = 34, Name = "Fabrizio" });
            var document = _serializer.ToDocument(bytes);

            Assert.AreEqual(EternalNodeKind.Object, document.Kind);
            Assert.AreEqual("Fabrizio", document["Name"].StringValue);
            Assert.IsTrue(document["Level"].TryGetInt64(out var level));
            Assert.AreEqual(34, level);

            document["Level"] = EternalNode.Number(99L);

            Assert.AreEqual(99, _serializer.Deserialize<Progress>(_serializer.FromDocument(document)).Level);
        }

        [Test]
        public void A_large_integer_survives_a_trip_through_the_tree()
        {
            // Routed through a double it would come back changed, and a tool that opened a save and
            // closed it again would silently rewrite an identifier.
            const long precise = 9007199254740993L;

            var bytes = Encoding.UTF8.GetBytes("{\"Id\":" + precise + "}");
            var document = _serializer.ToDocument(bytes);

            Assert.AreEqual(precise.ToString(CultureInfo.InvariantCulture), document["Id"].RawNumber);
            Assert.That(Encoding.UTF8.GetString(_serializer.FromDocument(document)), Does.Contain(precise.ToString()));
        }

        [Test]
        public void Arrays_and_nesting_survive_the_tree()
        {
            var bytes = Encoding.UTF8.GetBytes("{\"a\":[1,2,{\"b\":true}],\"c\":null}");
            var document = _serializer.ToDocument(bytes);

            Assert.AreEqual(3, document["a"].Items.Count);
            Assert.IsTrue(document["a"][2]["b"].BooleanValue);
            Assert.AreEqual(EternalNodeKind.Null, document["c"].Kind);

            var again = _serializer.ToDocument(_serializer.FromDocument(document));
            Assert.AreEqual(3, again["a"].Items.Count);
        }

        // --- Polymorphism without type names -------------------------------------------------

        private abstract class Reward
        {
            public string Label { get; set; }
        }

        private sealed class CoinReward : Reward
        {
            public int Amount { get; set; }
        }

        private sealed class SkinReward : Reward
        {
            public string SkinId { get; set; }
        }

        private sealed class Chest
        {
            public Reward Prize { get; set; }
            public List<Reward> Extras { get; set; }
        }

        private static NewtonsoftJsonSerializer WithRewards()
        {
            var converter = new EternalTypeDiscriminatorConverter<Reward>("kind")
                .Register<CoinReward>("coins")
                .Register<SkinReward>("skin");

            return new NewtonsoftJsonSerializer(settings => settings.Converters.Add(converter));
        }

        [Test]
        public void A_subtype_round_trips_behind_its_discriminator()
        {
            var serializer = WithRewards();

            var chest = new Chest
            {
                Prize = new CoinReward { Label = "Jackpot", Amount = 500 },
                Extras = new List<Reward> { new SkinReward { Label = "Rare", SkinId = "wizard-gold" } },
            };

            var json = Encoding.UTF8.GetString(serializer.Serialize(chest));

            // The name in the file is the game's, not the .NET type's — so renaming the class later
            // does not invalidate saves.
            Assert.That(json, Does.Contain("\"kind\":\"coins\""));
            Assert.That(json, Does.Not.Contain("CoinReward"));

            var read = serializer.Deserialize<Chest>(Encoding.UTF8.GetBytes(json));

            Assert.IsInstanceOf<CoinReward>(read.Prize);
            Assert.AreEqual(500, ((CoinReward)read.Prize).Amount);
            Assert.AreEqual("Jackpot", read.Prize.Label);

            Assert.IsInstanceOf<SkinReward>(read.Extras[0]);
            Assert.AreEqual("wizard-gold", ((SkinReward)read.Extras[0]).SkinId);
        }

        [Test]
        public void A_name_the_game_never_registered_is_refused()
        {
            // The closed list is the whole point: editing a save cannot add to it.
            var serializer = WithRewards();
            var hostile = Encoding.UTF8.GetBytes(
                "{\"Prize\":{\"kind\":\"System.Diagnostics.Process\",\"Label\":\"x\"}}");

            var error = Assert.Throws<JsonSerializationException>(
                () => serializer.Deserialize<Chest>(hostile));

            Assert.That(error.Message, Does.Contain("not a registered subtype"));
        }

        [Test]
        public void A_subtype_with_no_discriminator_is_refused()
        {
            var serializer = WithRewards();

            Assert.Throws<JsonSerializationException>(
                () => serializer.Deserialize<Chest>(Encoding.UTF8.GetBytes("{\"Prize\":{\"Label\":\"x\"}}")));
        }

        [Test]
        public void Writing_an_unregistered_subtype_fails_instead_of_writing_a_save_nothing_can_read()
        {
            var converter = new EternalTypeDiscriminatorConverter<Reward>("kind").Register<CoinReward>("coins");
            var serializer = new NewtonsoftJsonSerializer(settings => settings.Converters.Add(converter));

            Assert.Throws<JsonSerializationException>(
                () => serializer.Serialize(new Chest { Prize = new SkinReward { SkinId = "x" } }));
        }

        [Test]
        public void A_discriminator_name_means_one_type_forever()
        {
            var converter = new EternalTypeDiscriminatorConverter<Reward>().Register<CoinReward>("coins");

            Assert.Throws<ArgumentException>(() => converter.Register<SkinReward>("coins"));
            Assert.Throws<ArgumentException>(() => converter.Register<CoinReward>("money"));

            // Registering the same pairing again is harmless: startup code runs twice.
            Assert.DoesNotThrow(() => converter.Register<CoinReward>("coins"));
        }

        [Test]
        public void The_discriminator_is_not_left_behind_as_data()
        {
            var serializer = WithRewards();
            var read = serializer.Deserialize<Chest>(Encoding.UTF8.GetBytes(
                "{\"Prize\":{\"kind\":\"coins\",\"Label\":\"L\",\"Amount\":3}}"));

            Assert.AreEqual(3, ((CoinReward)read.Prize).Amount);
            Assert.AreEqual("L", read.Prize.Label);
        }

        [Test]
        public void A_null_subtype_is_allowed()
        {
            var serializer = WithRewards();
            var json = Encoding.UTF8.GetString(serializer.Serialize(new Chest()));

            Assert.IsNull(serializer.Deserialize<Chest>(Encoding.UTF8.GetBytes(json)).Prize);
        }
    }
}
#endif
