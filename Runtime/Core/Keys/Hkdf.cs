using System;
using System.Security.Cryptography;
using System.Text;

namespace NexusChaser.EternalDPS.Keys
{
    /// <summary>
    /// HKDF over SHA-256, as specified by RFC 5869.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written out here rather than taken from the framework because <c>System.Security.Cryptography.HKDF</c>
    /// only exists from .NET 5, and the core targets .NET Standard 2.1 so that it can run outside a
    /// game engine at all.
    /// </para>
    /// <para>
    /// <strong>This is frozen.</strong> Changing the derivation — the hash, the order of the
    /// operations, the fixed part of the info string — would produce different keys from the same
    /// input material, and every save already signed would stop verifying. It is exactly the kind
    /// of decision the package takes instead of each project, because getting it wrong once is
    /// unrecoverable.
    /// </para>
    /// <para>
    /// Correctness is not assumed: the test suite runs the known-answer vectors from Appendix A of
    /// the RFC. A hand-written primitive that nobody checked against the specification is worse than
    /// no primitive at all, because it looks like it works.
    /// </para>
    /// </remarks>
    public static class Hkdf
    {
        /// <summary>Output size of SHA-256, in bytes.</summary>
        public const int HashLength = 32;

        /// <summary>
        /// Longest output the construction can produce: the expand step counts iterations in a
        /// single byte, so it stops at 255 blocks.
        /// </summary>
        public const int MaxOutputLength = 255 * HashLength;

        /// <summary>
        /// Extract step: condenses possibly uneven input material into a uniform pseudorandom key.
        /// </summary>
        /// <param name="salt">
        /// Optional and public. When absent it defaults to a string of zeros the length of the hash,
        /// which is what the RFC prescribes — not an error.
        /// </param>
        /// <param name="inputKeyMaterial">The secret the game supplies.</param>
        public static byte[] Extract(byte[] salt, byte[] inputKeyMaterial)
        {
            if (inputKeyMaterial == null)
            {
                throw new ArgumentNullException(nameof(inputKeyMaterial));
            }

            var effectiveSalt = salt == null || salt.Length == 0 ? new byte[HashLength] : salt;

            using (var hmac = new HMACSHA256(effectiveSalt))
            {
                return hmac.ComputeHash(inputKeyMaterial);
            }
        }

        /// <summary>
        /// Expand step: stretches the pseudorandom key into as many bytes as were asked for.
        /// </summary>
        /// <param name="pseudoRandomKey">What <see cref="Extract"/> produced.</param>
        /// <param name="info">
        /// Context, bound into the output. Two different info values over the same key give two
        /// unrelated outputs, which is how per-purpose subkeys come for free.
        /// </param>
        /// <param name="length">How many bytes to produce.</param>
        public static byte[] Expand(byte[] pseudoRandomKey, byte[] info, int length)
        {
            if (pseudoRandomKey == null)
            {
                throw new ArgumentNullException(nameof(pseudoRandomKey));
            }

            if (length <= 0 || length > MaxOutputLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(length), length, "HKDF produces between 1 and " + MaxOutputLength + " bytes.");
            }

            var context = info ?? new byte[0];
            var output = new byte[length];
            var previous = new byte[0];
            var produced = 0;
            var counter = 1;

            using (var hmac = new HMACSHA256(pseudoRandomKey))
            {
                while (produced < length)
                {
                    // T(n) = HMAC(PRK, T(n-1) | info | n), with T(0) empty.
                    var block = new byte[previous.Length + context.Length + 1];

                    Buffer.BlockCopy(previous, 0, block, 0, previous.Length);
                    Buffer.BlockCopy(context, 0, block, previous.Length, context.Length);
                    block[block.Length - 1] = (byte)counter;

                    previous = hmac.ComputeHash(block);

                    var take = Math.Min(previous.Length, length - produced);
                    Buffer.BlockCopy(previous, 0, output, produced, take);

                    produced += take;
                    counter++;
                }
            }

            return output;
        }

        /// <summary>Both steps at once, which is how it is almost always used.</summary>
        public static byte[] DeriveBytes(byte[] inputKeyMaterial, byte[] salt, byte[] info, int length)
        {
            var pseudoRandomKey = Extract(salt, inputKeyMaterial);

            try
            {
                return Expand(pseudoRandomKey, info, length);
            }
            finally
            {
                Array.Clear(pseudoRandomKey, 0, pseudoRandomKey.Length);
            }
        }
    }

    /// <summary>
    /// The one supported way to turn a game's secret material into a signing key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The split of responsibilities here is deliberate: <strong>the package decides how material
    /// becomes a key, the game decides what the material is and where it lives</strong>. Freedom
    /// where a mistake is harmless, and none where a mistake cannot be undone. If every project
    /// improvised its own derivation, sooner or later one of them would feed a password through
    /// <c>System.Random</c> and there would be nothing to salvage afterwards.
    /// </para>
    /// <para>
    /// The input material must not be a literal string in the source. Anything written as a string
    /// constant shows up by running <c>strings</c> on the shipped binary. Assemble it at runtime
    /// from separated pieces, or read it from somewhere outside the build.
    /// </para>
    /// </remarks>
    public static class EternalKeyDerivation
    {
        /// <summary>
        /// The fixed part of the context string. Frozen: changing it changes every derived key.
        /// </summary>
        public const string InfoPrefix = "EternalDPS/v1/";

        /// <summary>
        /// Derives a signing key.
        /// </summary>
        /// <param name="id">
        /// The identifier the container will record. Also bound into the derivation, so rotating to
        /// a new identifier with the same material still produces an unrelated key.
        /// </param>
        /// <param name="inputKeyMaterial">The game's secret. At least 16 bytes.</param>
        /// <param name="salt">
        /// Optional, and not secret. A per-game constant is a good choice; it keeps two games that
        /// somehow share material from ending up with the same key.
        /// </param>
        /// <param name="purpose">
        /// Optional context, for deriving unrelated subkeys from one secret — per record kind, for
        /// instance. Part of the derivation, so it can never be changed after saves exist.
        /// </param>
        /// <param name="length">Key length, between the floor and what is useful.</param>
        /// <param name="state">Where the resulting key sits in its life.</param>
        /// <exception cref="ArgumentException">The material is missing or implausibly short.</exception>
        public static EternalKeyMaterial DeriveKey(
            byte id,
            byte[] inputKeyMaterial,
            byte[] salt = null,
            string purpose = null,
            int length = EternalKeyMaterial.MinimumLength,
            KeyState state = KeyState.Active)
        {
            if (inputKeyMaterial == null || inputKeyMaterial.Length < 16)
            {
                throw new ArgumentException(
                    "Key material needs at least 16 bytes of real entropy. Deriving from something " +
                    "shorter does not make it stronger; it only makes it look stronger.",
                    nameof(inputKeyMaterial));
            }

            if (id == EternalIds.None)
            {
                throw new ArgumentException("Key identifier 0 is reserved.", nameof(id));
            }

            var info = Encoding.UTF8.GetBytes(InfoPrefix + "key/" + id + "/" + (purpose ?? string.Empty));
            var derived = Hkdf.DeriveBytes(inputKeyMaterial, salt, info, length);

            try
            {
                return new EternalKeyMaterial(id, derived, state);
            }
            finally
            {
                // The key material lives inside EternalKeyMaterial now; this copy does not linger.
                Array.Clear(derived, 0, derived.Length);
            }
        }
    }
}
