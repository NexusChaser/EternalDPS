using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using NexusChaser.EternalDPS.Abstractions;
using NexusChaser.EternalDPS.Keys;

namespace NexusChaser.EternalDPS.Container
{
    /// <summary>
    /// Assembles a container: preamble, metadata, transformed body, signature.
    /// </summary>
    public static class ContainerWriter
    {
        /// <summary>
        /// Writes a signed container.
        /// </summary>
        /// <param name="serializedBody">
        /// The payload as the serialiser produced it. Transforms have not been applied yet; that
        /// happens here, so that the preamble and the body cannot disagree about what was done.
        /// </param>
        /// <param name="serializerId">Which serialiser produced it.</param>
        /// <param name="transforms">
        /// Applied in this order. The same order is recorded in the preamble, and reading undoes
        /// them back to front.
        /// </param>
        /// <param name="metadata">What the save says about itself. Optional but almost always present.</param>
        /// <param name="keys">Where the signing key comes from.</param>
        /// <exception cref="EternalKeyMissingException">There is no key to sign with.</exception>
        public static byte[] Write(
            byte[] serializedBody,
            byte serializerId,
            IReadOnlyList<IByteTransform> transforms,
            SaveMetadata metadata,
            IKeyProvider keys)
        {
            if (keys == null)
            {
                throw new EternalKeyMissingException(
                    "Signing needs an IKeyProvider and none was given. The package ships no key of " +
                    "its own — it lives in a public repository, so any key in it would be published " +
                    "with it. If this build really is meant to write unsigned saves, call " +
                    "WriteUnsigned and mean it.");
            }

            var key = keys.GetSigningKey();

            if (key == null)
            {
                throw new EternalKeyMissingException("The key provider returned no signing key.");
            }

            if (!key.CanSign)
            {
                throw new EternalKeyMissingException(
                    "The provider offered " + key + " as the signing key, but a key in that state " +
                    "may only verify existing saves. Signing needs an active key.");
            }

            return Build(serializedBody, serializerId, transforms, metadata, key);
        }

        /// <summary>
        /// Writes a container with no signature.
        /// </summary>
        /// <remarks>
        /// A development convenience, named so that it cannot happen by accident. An unsigned save
        /// looks identical to a signed one from the outside and verifies nothing, so a build that
        /// shipped this way would not be noticed until somebody edited a save.
        /// </remarks>
        public static byte[] WriteUnsigned(
            byte[] serializedBody,
            byte serializerId,
            IReadOnlyList<IByteTransform> transforms,
            SaveMetadata metadata)
        {
            return Build(serializedBody, serializerId, transforms, metadata, null);
        }

        private static byte[] Build(
            byte[] serializedBody,
            byte serializerId,
            IReadOnlyList<IByteTransform> transforms,
            SaveMetadata metadata,
            EternalKeyMaterial key)
        {
            if (serializedBody == null)
            {
                throw new ArgumentNullException(nameof(serializedBody));
            }

            if (serializerId == EternalIds.None)
            {
                throw new ArgumentException(
                    "A container has to name the serializer that wrote its body.", nameof(serializerId));
            }

            var transformCount = transforms?.Count ?? 0;

            if (transformCount > ContainerFormat.MaxTransformCount)
            {
                throw new EternalFormatException(
                    "A container can chain at most " + ContainerFormat.MaxTransformCount + " transforms.");
            }

            var body = serializedBody;
            var transformIds = new byte[transformCount];

            for (var i = 0; i < transformCount; i++)
            {
                var transform = transforms[i];

                if (transform == null)
                {
                    throw new ArgumentException("The transform chain contains a null entry.", nameof(transforms));
                }

                if (transform.TransformId == EternalIds.None)
                {
                    throw new ArgumentException(
                        transform.GetType().Name + " declares identifier 0, which is not a valid one.",
                        nameof(transforms));
                }

                body = transform.Apply(body);
                transformIds[i] = transform.TransformId;
            }

            var metadataBytes = metadata == null ? null : MetadataCodec.Encode(metadata);
            var metadataLength = metadataBytes?.Length ?? 0;

            var flags = ContainerFlags.None;

            if (metadataLength > 0)
            {
                flags |= ContainerFlags.HasMetadata;
            }

            if (key != null)
            {
                flags |= ContainerFlags.Signed;
            }

            var preamble = new ContainerPreamble(
                ContainerFormat.CurrentVersion,
                flags,
                metadataLength,
                body.Length,
                serializerId,
                key?.Id ?? EternalIds.None,
                transformIds);

            var file = new byte[preamble.ExpectedTotalLength];

            preamble.WriteTo(file);

            if (metadataLength > 0)
            {
                Array.Copy(metadataBytes, 0, file, preamble.MetadataOffset, metadataLength);
            }

            Array.Copy(body, 0, file, preamble.BodyOffset, body.Length);

            if (key != null)
            {
                Sign(file, preamble.SignatureOffset, key);
            }

            return file;
        }

        /// <summary>
        /// Rewrites a container's signature with the current key, leaving every byte of its
        /// contents alone.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is how a save moves off a retired key without the player doing anything. Nothing is
        /// decoded, decompressed or re-serialised: the preamble, the metadata and the body are the
        /// same bytes afterwards, and only the key identifier and the signature change.
        /// </para>
        /// <para>
        /// <strong>It verifies first, and that is not optional.</strong> Re-signing without checking
        /// would take a file somebody had edited and hand it back correctly signed with the current
        /// key — laundering exactly what the signature exists to catch.
        /// </para>
        /// </remarks>
        /// <param name="file">The container as it is on disk.</param>
        /// <param name="keys">Where the current key comes from.</param>
        /// <param name="resigned">The rewritten container. Null when nothing was rewritten.</param>
        /// <param name="report">What the verification found.</param>
        /// <returns>
        /// True when a rewritten container was produced. False when it did not verify, or when it
        /// was already signed with the current key and there was nothing to do.
        /// </returns>
        public static bool TryResign(byte[] file, IKeyProvider keys, out byte[] resigned, out IntegrityReport report)
        {
            resigned = null;
            report = ContainerReader.Verify(file, keys);

            if (!report.IsValid)
            {
                return false;
            }

            if (keys == null)
            {
                return false;
            }

            var current = keys.GetSigningKey();

            if (current == null || !current.CanSign)
            {
                throw new EternalKeyMissingException(
                    "Re-signing needs an active key and the provider offered none.");
            }

            ContainerReader.TryReadPreamble(file, out var preamble, out _);

            if (preamble.KeyId == current.Id)
            {
                return false;
            }

            var rewritten = (byte[])file.Clone();
            rewritten[ContainerFormat.OffsetKeyId] = current.Id;

            Sign(rewritten, preamble.SignatureOffset, current);

            resigned = rewritten;
            return true;
        }

        /// <summary>
        /// Signs everything written so far and appends the result.
        /// </summary>
        /// <remarks>
        /// The signature covers <strong>the preamble as well as the payload</strong>. Signing only
        /// the body would leave the transform list unprotected, and then removing the encryption
        /// identifier from a file would be enough to walk straight past the protection: the file
        /// would simply claim it had never been encrypted, and a reader would believe it.
        /// </remarks>
        internal static void Sign(byte[] file, int signatureOffset, EternalKeyMaterial key)
        {
            var material = key.CopyValue();

            try
            {
                using (var hmac = new HMACSHA256(material))
                {
                    var signature = hmac.ComputeHash(file, 0, signatureOffset);
                    Array.Copy(signature, 0, file, signatureOffset, ContainerFormat.SignatureLength);
                }
            }
            finally
            {
                // The copy does not outlive the signature it produced.
                Array.Clear(material, 0, material.Length);
            }
        }
    }
}
