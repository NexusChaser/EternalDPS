using System;
using System.Security.Cryptography;
using NexusChaser.EternalDPS.Keys;

namespace NexusChaser.EternalDPS.Container
{
    /// <summary>
    /// Takes a container apart, in the order that lets each step be trusted: header first, then
    /// metadata, and the body only once the signature has been checked.
    /// </summary>
    public static class ContainerReader
    {
        /// <summary>
        /// Reads the header. Applies no transform and verifies nothing, because the header is what
        /// says which transforms and which key are involved.
        /// </summary>
        public static bool TryReadPreamble(byte[] bytes, out ContainerPreamble preamble, out IntegrityReport failure)
        {
            return ContainerPreamble.TryParse(bytes, out preamble, out failure);
        }

        /// <summary>
        /// Checks a container's signature without decoding anything.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Nothing is deserialised, decompressed or decrypted: the body is hashed as the opaque
        /// bytes it is. That makes this cheap enough to run over a whole folder, which is what
        /// feeds an integrity report, and it means a save whose body is nonsense can still be
        /// checked, identified and listed.
        /// </para>
        /// <para>
        /// The comparison is constant time. A signature check that returns early on the first
        /// wrong byte tells an attacker how many leading bytes were right.
        /// </para>
        /// </remarks>
        /// <param name="bytes">The whole container.</param>
        /// <param name="keys">Where verification keys come from. Null reports the file as unsigned-verifiable.</param>
        public static IntegrityReport Verify(byte[] bytes, IKeyProvider keys)
        {
            if (!TryReadPreamble(bytes, out var preamble, out var failure))
            {
                return failure;
            }

            var available = bytes.Length;

            if (!preamble.IsSigned)
            {
                return IntegrityReport.Failed(
                    IntegrityStatus.Unsigned,
                    ContainerRegion.Signature,
                    "The container carries no signature, so nothing about it can be vouched for.",
                    preamble.Version, preamble.KeyId, preamble.MetadataLength, preamble.BodyLength, available);
            }

            if (keys == null || !keys.TryGetKey(preamble.KeyId, out var key) || key == null)
            {
                return IntegrityReport.Failed(
                    IntegrityStatus.UnknownKey,
                    ContainerRegion.Signature,
                    "The container was signed with key " + preamble.KeyId +
                    ", which this build does not have.",
                    preamble.Version, preamble.KeyId, preamble.MetadataLength, preamble.BodyLength, available);
            }

            if (!key.CanVerify)
            {
                return IntegrityReport.Failed(
                    IntegrityStatus.RejectedKey,
                    ContainerRegion.Signature,
                    "The container was signed with " + key + ", which is no longer accepted.",
                    preamble.Version, preamble.KeyId, preamble.MetadataLength, preamble.BodyLength, available);
            }

            var material = key.CopyValue();
            byte[] expected;

            try
            {
                using (var hmac = new HMACSHA256(material))
                {
                    expected = hmac.ComputeHash(bytes, 0, preamble.SignatureOffset);
                }
            }
            finally
            {
                Array.Clear(material, 0, material.Length);
            }

            var matches = Bytes.ConstantTimeEquals(
                expected, 0, bytes, preamble.SignatureOffset, ContainerFormat.SignatureLength);

            if (!matches)
            {
                return IntegrityReport.Failed(
                    IntegrityStatus.SignatureMismatch,
                    ContainerRegion.Signature,
                    "The recomputed signature does not match the stored one: these are not the bytes that were written.",
                    preamble.Version, preamble.KeyId, preamble.MetadataLength, preamble.BodyLength, available);
            }

            return IntegrityReport.Valid(
                preamble.Version, preamble.KeyId, preamble.MetadataLength, preamble.BodyLength, available,
                key.State);
        }

        /// <summary>
        /// Reads the metadata without touching the body.
        /// </summary>
        /// <remarks>
        /// Deliberately does not verify. A slot list has to be able to draw a damaged save so the
        /// player can see it and delete it; refusing to read its name because its body is broken
        /// makes the save disappear from the interface and leaves them stuck with it.
        /// Anything that acts on the contents verifies first.
        /// </remarks>
        /// <param name="bytes">The whole container.</param>
        /// <param name="includeThumbnail">False to skip the preview image.</param>
        public static LoadResult<SaveMetadata> ReadMetadata(byte[] bytes, bool includeThumbnail = true)
        {
            if (!TryReadPreamble(bytes, out var preamble, out var failure))
            {
                return LoadResult.FromIntegrity<SaveMetadata>(failure);
            }

            if (!preamble.HasMetadata)
            {
                return LoadResult.Failed<SaveMetadata>(
                    LoadStatus.Corrupt, "The container carries no metadata block.");
            }

            try
            {
                var metadata = MetadataCodec.Decode(
                    bytes, preamble.MetadataOffset, preamble.MetadataLength, includeThumbnail);

                return LoadResult.Ok(metadata);
            }
            catch (EternalFormatException error)
            {
                return LoadResult.Failed<SaveMetadata>(LoadStatus.Corrupt, error.Message);
            }
        }

        /// <summary>
        /// Verifies the container and returns the body with every transform undone.
        /// </summary>
        /// <remarks>
        /// Transforms are undone <strong>back to front</strong>: the preamble lists them in the
        /// order they were applied, so the last one applied is the first one removed.
        /// </remarks>
        /// <param name="bytes">The whole container.</param>
        /// <param name="transforms">What this build knows how to undo.</param>
        /// <param name="keys">Where verification keys come from.</param>
        /// <param name="report">What the verification pass found, whether or not it passed.</param>
        public static LoadResult<byte[]> ReadBody(
            byte[] bytes,
            TransformRegistry transforms,
            IKeyProvider keys,
            out IntegrityReport report)
        {
            report = Verify(bytes, keys);

            if (!report.IsValid)
            {
                return LoadResult.FromIntegrity<byte[]>(report);
            }

            // Verify already parsed this successfully; it cannot fail here.
            TryReadPreamble(bytes, out var preamble, out _);

            var body = new byte[preamble.BodyLength];
            Array.Copy(bytes, preamble.BodyOffset, body, 0, preamble.BodyLength);

            for (var i = preamble.TransformIds.Length - 1; i >= 0; i--)
            {
                var id = preamble.TransformIds[i];

                if (transforms == null || !transforms.TryGet(id, out var transform))
                {
                    return LoadResult.Failed<byte[]>(
                        LoadStatus.UnknownTransform,
                        "The container was written with transform 0x" + id.ToString("X2") +
                        ", which this build does not have. An optional module is missing.");
                }

                try
                {
                    body = transform.Invert(body);
                }
                catch (Exception error)
                {
                    // The signature was valid, so these are exactly the bytes that were written and
                    // they still will not decode. That makes it our bug, not the player's disk.
                    return LoadResult.Failed<byte[]>(
                        LoadStatus.Corrupt,
                        "Transform 0x" + id.ToString("X2") + " could not undo a body that verified: " +
                        error.Message);
                }
            }

            return LoadResult.Ok(body);
        }
    }
}
