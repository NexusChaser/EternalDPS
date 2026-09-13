using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NexusChaser.EternalDPS.Abstractions;
using NexusChaser.EternalDPS.Keys;

namespace NexusChaser.EternalDPS.Tests
{
    /// <summary>
    /// The smallest implementations that satisfy each core contract. They exist to exercise the
    /// contracts, not to be good implementations of them.
    /// </summary>
    public static class TestDoubles
    {
        /// <summary>Deterministic key material of a given length, so tests repeat exactly.</summary>
        public static byte[] KeyBytes(byte seed, int length = EternalKeyMaterial.MinimumLength)
        {
            var bytes = new byte[length];

            for (var i = 0; i < length; i++)
            {
                bytes[i] = (byte)(seed + i);
            }

            return bytes;
        }
    }

    /// <summary>A store that keeps everything in a dictionary.</summary>
    public sealed class InMemoryStore : IStore
    {
        private readonly Dictionary<string, byte[]> _files = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        /// <summary>How many times anything asked for a flush.</summary>
        public int FlushCount { get; private set; }

        /// <inheritdoc />
        public StoreCapabilities Capabilities { get; set; } = StoreCapabilities.All;

        /// <summary>
        /// The stored array itself, bypassing the store API. Editing what comes back edits the
        /// record in place, which is how a test simulates a file being tampered with on disk.
        /// </summary>
        public byte[] Peek(string relativePath)
        {
            return _files.TryGetValue(relativePath, out var data) ? data : null;
        }

        /// <summary>Puts bytes at a path, bypassing the store API. For arranging a test.</summary>
        public void Poke(string relativePath, byte[] data)
        {
            _files[relativePath] = Copy(data);
        }

        /// <summary>
        /// A real store hands out a fresh buffer every time, because the bytes live on a disk and
        /// not in this process. Copying here keeps that true, so nothing under test can accidentally
        /// depend on two records sharing an array.
        /// </summary>
        private static byte[] Copy(byte[] data)
        {
            return data == null ? null : (byte[])data.Clone();
        }

        /// <inheritdoc />
        public Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken)
        {
            return Task.FromResult(_files.ContainsKey(relativePath));
        }

        /// <inheritdoc />
        public Task<byte[]> ReadAsync(string relativePath, CancellationToken cancellationToken)
        {
            return Task.FromResult(Copy(_files.TryGetValue(relativePath, out var data) ? data : null));
        }

        /// <inheritdoc />
        public Task WriteAsync(string relativePath, byte[] data, CancellationToken cancellationToken)
        {
            _files[relativePath] = Copy(data);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken)
        {
            _files.Remove(relativePath);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken cancellationToken)
        {
            IReadOnlyList<string> matches = _files.Keys
                .Where(path => path.StartsWith(prefix ?? string.Empty, StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            return Task.FromResult(matches);
        }

        /// <inheritdoc />
        public Task FlushAsync(CancellationToken cancellationToken)
        {
            FlushCount++;
            return Task.CompletedTask;
        }
    }

    /// <summary>A serialiser that only handles strings, plus its tree view.</summary>
    public sealed class Utf8StringSerializer : IDocumentSerializer
    {
        /// <summary>Creates one, optionally claiming a different format id.</summary>
        public Utf8StringSerializer(byte formatId = 0x80)
        {
            FormatId = formatId;
        }

        /// <inheritdoc />
        public byte FormatId { get; }

        /// <inheritdoc />
        public byte[] Serialize(object value, Type type)
        {
            return Encoding.UTF8.GetBytes((string)value ?? string.Empty);
        }

        /// <inheritdoc />
        public object Deserialize(byte[] data, Type type)
        {
            return Encoding.UTF8.GetString(data);
        }

        /// <inheritdoc />
        public EternalNode ToDocument(byte[] data)
        {
            var document = EternalNode.Object();
            document["value"] = EternalNode.String(Encoding.UTF8.GetString(data));
            return document;
        }

        /// <inheritdoc />
        public byte[] FromDocument(EternalNode document)
        {
            return Encoding.UTF8.GetBytes(document["value"].StringValue);
        }
    }

    /// <summary>A serialiser that always throws, to exercise the corrupt-body path.</summary>
    public sealed class ThrowingSerializer : ISerializer
    {
        /// <inheritdoc />
        public byte FormatId => 0x81;

        /// <inheritdoc />
        public byte[] Serialize(object value, Type type)
        {
            return new byte[] { 1, 2, 3 };
        }

        /// <inheritdoc />
        public object Deserialize(byte[] data, Type type)
        {
            throw new InvalidOperationException("This serializer cannot read anything.");
        }
    }

    /// <summary>A transform that is its own inverse.</summary>
    public sealed class ReversingTransform : IByteTransform
    {
        /// <summary>Creates one, optionally claiming a different identifier.</summary>
        public ReversingTransform(byte transformId = 0x80)
        {
            TransformId = transformId;
        }

        /// <inheritdoc />
        public byte TransformId { get; }

        /// <inheritdoc />
        public byte[] Apply(byte[] input)
        {
            var output = (byte[])input.Clone();
            Array.Reverse(output);
            return output;
        }

        /// <inheritdoc />
        public byte[] Invert(byte[] input)
        {
            return Apply(input);
        }
    }

    /// <summary>A clock that only moves when a test moves it.</summary>
    public sealed class FixedClock : IClock
    {
        /// <summary>Creates a clock stopped at a moment.</summary>
        public FixedClock(DateTimeOffset now)
        {
            UtcNow = now;
        }

        /// <inheritdoc />
        public DateTimeOffset UtcNow { get; set; }

        /// <summary>Moves time forward.</summary>
        public void Advance(TimeSpan amount)
        {
            UtcNow = UtcNow.Add(amount);
        }
    }

    /// <summary>A log that remembers what it was told, so a test can assert on it.</summary>
    public sealed class RecordingLog : IEternalLog
    {
        /// <summary>Everything logged, in order.</summary>
        public List<string> Entries { get; } = new List<string>();

        /// <inheritdoc />
        public void Log(EternalLogLevel level, string message, Exception exception)
        {
            Entries.Add(level + ": " + message);
        }

        /// <summary>True when anything at this level mentions the given text.</summary>
        public bool Contains(EternalLogLevel level, string fragment)
        {
            return Entries.Any(entry =>
                entry.StartsWith(level + ":", StringComparison.Ordinal) &&
                entry.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }

    /// <summary>A key set assembled by hand.</summary>
    public sealed class TestKeyProvider : IKeyProvider
    {
        private readonly List<EternalKeyMaterial> _keys;

        /// <summary>Creates a provider holding the given keys.</summary>
        public TestKeyProvider(params EternalKeyMaterial[] keys)
        {
            _keys = new List<EternalKeyMaterial>(keys ?? new EternalKeyMaterial[0]);
        }

        /// <summary>A provider with one active key.</summary>
        public static TestKeyProvider WithSingleKey(byte id = 1)
        {
            return new TestKeyProvider(new EternalKeyMaterial(id, TestDoubles.KeyBytes(id)));
        }

        /// <inheritdoc />
        public EternalKeyMaterial GetSigningKey()
        {
            var active = _keys.FirstOrDefault(key => key.CanSign);

            if (active == null)
            {
                throw new EternalKeyMissingException("This provider holds no key that may sign.");
            }

            return active;
        }

        /// <inheritdoc />
        public bool TryGetKey(byte keyId, out EternalKeyMaterial key)
        {
            key = _keys.FirstOrDefault(candidate => candidate.Id == keyId);
            return key != null;
        }
    }
}
