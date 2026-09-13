using System;

namespace NexusChaser.EternalDPS.Keys
{
    /// <summary>
    /// Where a key is in its life. A key is retired in stages, never switched off in one go.
    /// </summary>
    /// <remarks>
    /// By the time a key reaches <see cref="Rejected"/>, almost every live save has already been
    /// re-signed with the current one, because that happens by itself on the next save. The stages
    /// exist so that retiring a key is a slope and not a cliff that strands the players who did not
    /// open the game that week.
    /// </remarks>
    public enum KeyState
    {
        /// <summary>The current key. New saves are signed with this one.</summary>
        Active = 0,

        /// <summary>Still verifies old saves, never signs new ones.</summary>
        ReadOnly = 1,

        /// <summary>Still verifies, and the game is told so it can warn or report.</summary>
        Warn = 2,

        /// <summary>No longer accepted. Files signed with it fail verification.</summary>
        Rejected = 3,
    }

    /// <summary>
    /// One key, its identifier and its state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This package ships no key and never will.</strong> It lives in a public repository,
    /// so any key inside it would be published alongside it, and signatures would protect nothing
    /// in any game that used it. Each game supplies its own through an
    /// <see cref="IKeyProvider"/>, out of its own private repository or CI secret.
    /// </para>
    /// <para>
    /// The material must not be a literal string in the source either — that shows up by running
    /// <c>strings</c> on the binary. Derive it at runtime from separated constants.
    /// </para>
    /// </remarks>
    public sealed class EternalKeyMaterial
    {
        /// <summary>
        /// Shortest key the package accepts. Below this there is nothing to argue about: it is not
        /// a key, it is a password someone typed.
        /// </summary>
        public const int MinimumLength = 32;

        /// <summary>
        /// Longest key worth using. HMAC hashes anything longer than SHA-256's 64 byte block down
        /// before using it, so past this point a longer key is only longer, never stronger.
        /// </summary>
        public const int UsefulMaximumLength = 64;

        private readonly byte[] _value;

        /// <summary>Creates a key.</summary>
        /// <param name="id">
        /// What the container will record in its <c>keyId</c> byte. Unique for the life of the
        /// game, and never reused for different material.
        /// </param>
        /// <param name="value">The key bytes. Copied, so the caller may clear its own array.</param>
        /// <param name="state">Where this key is in its life.</param>
        /// <exception cref="ArgumentException">The material is missing or too short.</exception>
        public EternalKeyMaterial(byte id, byte[] value, KeyState state = KeyState.Active)
        {
            if (value == null || value.Length == 0)
            {
                throw new ArgumentException("A key needs material.", nameof(value));
            }

            if (value.Length < MinimumLength)
            {
                throw new ArgumentException(
                    "A key must be at least " + MinimumLength + " bytes; this one is " + value.Length +
                    ". The package imposes the floor because getting it wrong cannot be fixed afterwards.",
                    nameof(value));
            }

            Id = id;
            State = state;
            _value = (byte[])value.Clone();
        }

        /// <summary>What the container records, so a reader knows which key to verify with.</summary>
        public byte Id { get; }

        /// <summary>Where this key is in its life.</summary>
        public KeyState State { get; }

        /// <summary>True when this key may sign new saves.</summary>
        public bool CanSign => State == KeyState.Active;

        /// <summary>True when this key may still verify existing saves.</summary>
        public bool CanVerify => State != KeyState.Rejected;

        /// <summary>
        /// The key material. A copy every time: handing out the array itself would let a caller
        /// clear or edit the live key by accident.
        /// </summary>
        public byte[] CopyValue()
        {
            return (byte[])_value.Clone();
        }

        /// <summary>Length of the material, without exposing it.</summary>
        public int Length => _value.Length;

        public override string ToString()
        {
            // Never the material, not even a prefix of it. This ends up in logs.
            return "key " + Id + " (" + State + ", " + _value.Length + " bytes)";
        }
    }

    /// <summary>
    /// Supplies the game's signing keys: the current one, and every retired one still needed to
    /// read older saves.
    /// </summary>
    /// <remarks>
    /// A save is always <strong>written with the newest key and read with whichever key the file
    /// names</strong>. That is what resolves the tension between a key stable enough that old saves
    /// keep loading and a key that can be replaced when it leaks: the answer is not one key, it is
    /// a versioned set.
    /// </remarks>
    public interface IKeyProvider
    {
        /// <summary>
        /// The key new saves are signed with.
        /// </summary>
        /// <exception cref="EternalKeyMissingException">The game supplied no usable key.</exception>
        EternalKeyMaterial GetSigningKey();

        /// <summary>
        /// Looks up the key a file names in its preamble.
        /// </summary>
        /// <returns>False when this build does not have that key at all.</returns>
        bool TryGetKey(byte keyId, out EternalKeyMaterial key);
    }

    /// <summary>
    /// Thrown when the core is asked to sign and there is no key to sign with.
    /// </summary>
    /// <remarks>
    /// This fails loudly on purpose. The quiet alternative — writing the save unsigned — would ship
    /// a build whose saves look fine and verify nothing, and nobody would notice until someone
    /// edited one.
    /// </remarks>
    public sealed class EternalKeyMissingException : Exception
    {
        /// <summary>Creates the exception with a description of what was missing.</summary>
        public EternalKeyMissingException(string message) : base(message)
        {
        }
    }
}
