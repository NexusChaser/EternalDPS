using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NexusChaser.EternalDPS.Abstractions;
using NUnit.Framework;

namespace NexusChaser.EternalDPS.Tests
{
    /// <summary>
    /// Proves the core interfaces can actually be implemented, and that the capability helpers
    /// behave. The fakes here are deliberately the smallest thing that satisfies each contract:
    /// the point is the shape of the contract, not the implementation.
    /// </summary>
    public class AbstractionTests
    {
        [Test]
        public void A_capability_set_reports_what_it_holds()
        {
            var full = StoreCapabilities.All;

            Assert.IsTrue(full.Supports(StoreCapabilities.AtomicReplace));
            Assert.IsTrue(full.Supports(StoreCapabilities.List | StoreCapabilities.Delete));
            Assert.IsTrue(StoreCapabilities.None.Supports(StoreCapabilities.None));
        }

        [Test]
        public void A_partial_capability_set_reports_what_it_lacks()
        {
            // What a browser store looks like: it can do everything except replace atomically.
            var web = StoreCapabilities.All & ~StoreCapabilities.AtomicReplace;

            Assert.IsFalse(web.Supports(StoreCapabilities.AtomicReplace));
            Assert.IsTrue(web.Supports(StoreCapabilities.List));
        }

        [Test]
        public void Requiring_a_missing_capability_names_it()
        {
            var steam = StoreCapabilities.List | StoreCapabilities.Delete;

            Assert.DoesNotThrow(() => steam.Require(StoreCapabilities.List, "Listing slots"));

            var error = Assert.Throws<NotSupportedException>(
                () => steam.Require(StoreCapabilities.RandomAccess, "Reading metadata only"));

            // A NotSupportedException with nothing in it is why nobody can debug these.
            Assert.That(error.Message, Does.Contain("RandomAccess"));
            Assert.That(error.Message, Does.Contain("Reading metadata only"));
        }

        [Test]
        public async Task A_store_round_trips_bytes_under_a_key_path()
        {
            var store = new InMemoryStore();
            var key = EternalKey.SlotSession("save");
            var payload = Encoding.UTF8.GetBytes("board state");

            Assert.IsFalse(await store.ExistsAsync(key.RelativePath));

            await store.WriteAsync(key.RelativePath, payload);
            await store.FlushAsync();

            Assert.IsTrue(await store.ExistsAsync(key.RelativePath));
            CollectionAssert.AreEqual(payload, await store.ReadAsync(key.RelativePath));
        }

        [Test]
        public async Task Reading_something_that_is_not_there_returns_null_rather_than_throwing()
        {
            var store = new InMemoryStore();

            Assert.IsNull(await store.ReadAsync("account/missing.etm"));
        }

        [Test]
        public async Task Deleting_something_that_is_not_there_succeeds()
        {
            // The caller asked for the record to be gone. It is gone.
            var store = new InMemoryStore();

            Assert.DoesNotThrowAsync(() => store.DeleteAsync("account/missing.etm"));
            Assert.IsFalse(await store.ExistsAsync("account/missing.etm"));
        }

        [Test]
        public async Task Listing_is_scoped_to_the_prefix()
        {
            var store = new InMemoryStore();
            var slot = SlotId.New();

            await store.WriteAsync(EternalKey.MachineSettings("display").RelativePath, new byte[1]);
            await store.WriteAsync(EternalKey.AccountSettings("prefs").RelativePath, new byte[1]);
            await store.WriteAsync(EternalKey.SlotProgress("save", slot).RelativePath, new byte[1]);

            var everything = await store.ListAsync(string.Empty);
            var slots = await store.ListAsync("slots/");

            Assert.AreEqual(3, everything.Count);
            Assert.AreEqual(1, slots.Count);
            Assert.That(slots[0], Does.StartWith("slots/" + slot));
        }

        [Test]
        public void A_transform_inverts_itself_including_on_an_empty_input()
        {
            // The empty case is where naive implementations fall over, and an empty payload is a
            // perfectly ordinary thing to save.
            var transform = new ReversingTransform();

            foreach (var input in new[] { new byte[0], new byte[] { 1 }, new byte[] { 1, 2, 3, 4 } })
            {
                CollectionAssert.AreEqual(input, transform.Invert(transform.Apply(input)));
            }
        }

        [Test]
        public void A_serializer_round_trips_through_its_generic_wrapper()
        {
            ISerializer serializer = new Utf8StringSerializer();

            var bytes = serializer.Serialize("hello");

            Assert.AreEqual("hello", serializer.Deserialize<string>(bytes));
            Assert.AreEqual(0x80, serializer.FormatId);
        }

        [Test]
        public void A_document_serializer_exposes_an_editable_tree()
        {
            var serializer = new Utf8StringSerializer();
            var document = serializer.ToDocument(serializer.Serialize("hello"));

            Assert.AreEqual(EternalNodeKind.Object, document.Kind);
            Assert.AreEqual("hello", document["value"].StringValue);

            document["value"] = EternalNode.String("edited");

            Assert.AreEqual("edited", serializer.Deserialize<string>(serializer.FromDocument(document)));
        }

        [Test]
        public void Numbers_in_a_tree_keep_their_exact_value()
        {
            // A 64-bit identifier routed through a double comes back as a different number. Opening
            // a save in a tool and closing it again must not change what it says.
            const long precise = 9007199254740993L;
            var node = EternalNode.Number(precise);

            Assert.IsTrue(node.TryGetInt64(out var readBack));
            Assert.AreEqual(precise, readBack);
            Assert.AreEqual("9007199254740993", node.RawNumber);
        }

        [Test]
        public void Asking_a_node_for_the_wrong_kind_fails_loudly()
        {
            Assert.Throws<InvalidOperationException>(() => _ = EternalNode.String("x").BooleanValue);
        }

        [Test]
        public void A_fake_clock_can_stand_in_for_the_real_one()
        {
            // Timestamps decide which of two saves is newer, so tests have to be able to move time
            // without waiting for it.
            var clock = new FixedClock(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));

            Assert.AreEqual(2026, clock.UtcNow.Year);
            Assert.AreEqual(TimeSpan.Zero, clock.UtcNow.Offset);
        }

        [Test]
        public void The_null_log_accepts_everything_and_keeps_nothing()
        {
            Assert.DoesNotThrow(() => NullLog.Instance.Error("ignored", new Exception()));
        }

        [Test]
        public void Log_extensions_tolerate_a_null_log()
        {
            // Nothing in the core should have to null-check before logging.
            IEternalLog log = null;

            Assert.DoesNotThrow(() => log.Warning("ignored"));
        }

        /// <summary>Everything a store has to be, held in a dictionary.</summary>
        private sealed class InMemoryStore : IStore
        {
            private readonly Dictionary<string, byte[]> _files = new Dictionary<string, byte[]>(StringComparer.Ordinal);

            public StoreCapabilities Capabilities => StoreCapabilities.All;

            public Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken)
            {
                return Task.FromResult(_files.ContainsKey(relativePath));
            }

            public Task<byte[]> ReadAsync(string relativePath, CancellationToken cancellationToken)
            {
                return Task.FromResult(_files.TryGetValue(relativePath, out var data) ? data : null);
            }

            public Task WriteAsync(string relativePath, byte[] data, CancellationToken cancellationToken)
            {
                _files[relativePath] = data;
                return Task.CompletedTask;
            }

            public Task DeleteAsync(string relativePath, CancellationToken cancellationToken)
            {
                _files.Remove(relativePath);
                return Task.CompletedTask;
            }

            public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken cancellationToken)
            {
                IReadOnlyList<string> matches = _files.Keys
                    .Where(path => path.StartsWith(prefix ?? string.Empty, StringComparison.Ordinal))
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToList();

                return Task.FromResult(matches);
            }

            public Task FlushAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }

        /// <summary>A transform that is its own inverse. Enough to exercise the contract.</summary>
        private sealed class ReversingTransform : IByteTransform
        {
            public byte TransformId => 0x80;

            public byte[] Apply(byte[] input)
            {
                var output = (byte[])input.Clone();
                Array.Reverse(output);
                return output;
            }

            public byte[] Invert(byte[] input)
            {
                return Apply(input);
            }
        }

        /// <summary>A serialiser that only knows how to handle strings, and its tree view.</summary>
        private sealed class Utf8StringSerializer : IDocumentSerializer
        {
            public byte FormatId => 0x80;

            public byte[] Serialize(object value, Type type)
            {
                return Encoding.UTF8.GetBytes((string)value ?? string.Empty);
            }

            public object Deserialize(byte[] data, Type type)
            {
                return Encoding.UTF8.GetString(data);
            }

            public EternalNode ToDocument(byte[] data)
            {
                var document = EternalNode.Object();
                document["value"] = EternalNode.String(Encoding.UTF8.GetString(data));
                return document;
            }

            public byte[] FromDocument(EternalNode document)
            {
                return Encoding.UTF8.GetBytes(document["value"].StringValue);
            }
        }

        /// <summary>A clock that never moves.</summary>
        private sealed class FixedClock : IClock
        {
            public FixedClock(DateTimeOffset now)
            {
                UtcNow = now;
            }

            public DateTimeOffset UtcNow { get; }
        }
    }
}
