using System;
using System.Collections.Generic;

namespace NexusChaser.EternalDPS.Container
{
    /// <summary>
    /// Encodes and decodes the metadata block.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This encoding is fixed and built into the core — it does not go through the
    /// pluggable <c>ISerializer</c>.</strong> That is deliberate. The metadata is what answers "is
    /// this file mine, and can I adopt it?", and a build has to be able to answer that for a file
    /// written by a build whose serialiser it does not have compiled in. If the metadata used the
    /// pluggable serialiser, a missing optional module would make a save unreadable <em>and</em>
    /// unidentifiable, so it could not even be listed or deleted.
    /// </para>
    /// <para>
    /// It is also not compressed. The block is a few hundred bytes, so compression would save
    /// nothing worth an inflate on every entry of a slot list, and the one part that is large — the
    /// thumbnail — arrives already compressed as PNG or JPEG.
    /// </para>
    /// <para>
    /// Encoding is deterministic: custom fields are written in ordinal key order, so the same
    /// metadata always produces the same bytes. Golden-file tests depend on that.
    /// </para>
    /// <code>
    ///   0  1  metadataVersion
    ///   1 16  productId (RFC 4122 order)
    ///  17  4  schemaVersion
    ///  21  8  savedAtUtc, Unix milliseconds
    ///  29  8  playtime, milliseconds
    ///  37  4  thumbnailOffset, from the start of this block; 0 when absent
    ///  41  4  thumbnailLength
    ///  45  .. appVersion, slotName          length-prefixed UTF-8
    ///      .. customCount, then key/value pairs in ordinal key order
    ///      .. thumbnail bytes
    /// </code>
    /// </remarks>
    public static class MetadataCodec
    {
        /// <summary>Version of this block's own encoding.</summary>
        public const byte CurrentVersion = 1;

        /// <summary>Highest block version this build can read.</summary>
        public const byte MaxSupportedVersion = 1;

        private const int OffsetVersion = 0;
        private const int OffsetProductId = 1;
        private const int OffsetSchemaVersion = 17;
        private const int OffsetSavedAt = 21;
        private const int OffsetPlaytime = 29;
        private const int OffsetThumbnailOffset = 37;
        private const int OffsetThumbnailLength = 41;
        private const int FixedHeaderLength = 45;

        private static readonly DateTimeOffset UnixEpoch =
            new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

        /// <summary>Turns metadata into the bytes of a metadata block.</summary>
        /// <exception cref="ArgumentNullException">No metadata was given.</exception>
        /// <exception cref="EternalFormatException">The block would exceed what the preamble can describe.</exception>
        public static byte[] Encode(SaveMetadata metadata)
        {
            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            var custom = SortedCustomFields(metadata);
            var thumbnailLength = metadata.HasThumbnail ? metadata.Thumbnail.Length : 0;

            var length = FixedHeaderLength
                         + Bytes.MeasureString(metadata.AppVersion)
                         + Bytes.MeasureString(metadata.SlotName)
                         + 2;

            foreach (var pair in custom)
            {
                length += Bytes.MeasureString(pair.Key) + Bytes.MeasureString(pair.Value);
            }

            var thumbnailOffset = length;
            length += thumbnailLength;

            if (length > ContainerFormat.MaxMetadataLength)
            {
                throw new EternalFormatException(
                    "The metadata block would be " + length + " bytes and the format allows " +
                    ContainerFormat.MaxMetadataLength +
                    ". A thumbnail is almost always what pushes it over; use a smaller one.");
            }

            var buffer = new byte[length];

            buffer[OffsetVersion] = CurrentVersion;
            Bytes.WriteGuid(buffer, OffsetProductId, metadata.ProductId);
            Bytes.WriteUInt32(buffer, OffsetSchemaVersion, metadata.SchemaVersion);
            Bytes.WriteInt64(buffer, OffsetSavedAt, ToUnixMilliseconds(metadata.SavedAtUtc));
            Bytes.WriteInt64(buffer, OffsetPlaytime, (long)metadata.Playtime.TotalMilliseconds);
            Bytes.WriteUInt32(buffer, OffsetThumbnailOffset, thumbnailLength == 0 ? 0u : (uint)thumbnailOffset);
            Bytes.WriteUInt32(buffer, OffsetThumbnailLength, (uint)thumbnailLength);

            var cursor = FixedHeaderLength;
            cursor = Bytes.WriteString(buffer, cursor, metadata.AppVersion);
            cursor = Bytes.WriteString(buffer, cursor, metadata.SlotName);

            Bytes.WriteUInt16(buffer, cursor, (ushort)custom.Count);
            cursor += 2;

            foreach (var pair in custom)
            {
                cursor = Bytes.WriteString(buffer, cursor, pair.Key);
                cursor = Bytes.WriteString(buffer, cursor, pair.Value);
            }

            if (thumbnailLength > 0)
            {
                Array.Copy(metadata.Thumbnail, 0, buffer, cursor, thumbnailLength);
            }

            return buffer;
        }

        /// <summary>
        /// Reads a metadata block out of a container.
        /// </summary>
        /// <param name="bytes">The whole container.</param>
        /// <param name="offset">Where the block starts.</param>
        /// <param name="length">How long the preamble says it is.</param>
        /// <param name="includeThumbnail">
        /// False to skip the image. A slot list that only draws names and playtimes has no reason
        /// to copy six thumbnails it will not show yet.
        /// </param>
        /// <exception cref="EternalFormatException">The block is malformed or from a newer build.</exception>
        public static SaveMetadata Decode(byte[] bytes, int offset, int length, bool includeThumbnail = true)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            Bytes.Require(offset, length, bytes.Length, "the metadata block");

            var limit = offset + length;

            if (length < FixedHeaderLength)
            {
                throw new EternalFormatException(
                    "The metadata block is " + length + " bytes, shorter than its own header.");
            }

            var version = bytes[offset + OffsetVersion];

            if (version > MaxSupportedVersion)
            {
                throw new EternalFormatException(
                    "The metadata block is version " + version + " and this build reads up to " +
                    MaxSupportedVersion + ".");
            }

            var metadata = new SaveMetadata
            {
                ProductId = Bytes.ReadGuid(bytes, offset + OffsetProductId),
                SchemaVersion = Bytes.ReadUInt32(bytes, offset + OffsetSchemaVersion),
                SavedAtUtc = FromUnixMilliseconds(Bytes.ReadInt64(bytes, offset + OffsetSavedAt)),
                Playtime = TimeSpan.FromMilliseconds(Bytes.ReadInt64(bytes, offset + OffsetPlaytime)),
            };

            var thumbnailOffset = Bytes.ReadUInt32(bytes, offset + OffsetThumbnailOffset);
            var thumbnailLength = Bytes.ReadUInt32(bytes, offset + OffsetThumbnailLength);

            var cursor = offset + FixedHeaderLength;

            cursor = Bytes.ReadString(bytes, cursor, limit, out var appVersion);
            cursor = Bytes.ReadString(bytes, cursor, limit, out var slotName);

            metadata.AppVersion = appVersion;
            metadata.SlotName = slotName;

            Bytes.Require(cursor, 2, limit, "the custom field count");
            var customCount = Bytes.ReadUInt16(bytes, cursor);
            cursor += 2;

            for (var i = 0; i < customCount; i++)
            {
                cursor = Bytes.ReadString(bytes, cursor, limit, out var key);
                cursor = Bytes.ReadString(bytes, cursor, limit, out var value);
                metadata.Custom[key] = value;
            }

            if (thumbnailLength > 0 && includeThumbnail)
            {
                // The offsets come out of the file, so they get bounds-checked like anything else.
                if (thumbnailOffset > int.MaxValue || thumbnailLength > int.MaxValue)
                {
                    throw new EternalFormatException("The thumbnail offsets do not fit in this runtime.");
                }

                var start = offset + (int)thumbnailOffset;
                Bytes.Require(start, (int)thumbnailLength, limit, "the thumbnail");

                metadata.Thumbnail = new byte[thumbnailLength];
                Array.Copy(bytes, start, metadata.Thumbnail, 0, (int)thumbnailLength);
            }

            return metadata;
        }

        private static List<KeyValuePair<string, string>> SortedCustomFields(SaveMetadata metadata)
        {
            var fields = new List<KeyValuePair<string, string>>();

            if (!metadata.HasCustomFields)
            {
                return fields;
            }

            foreach (var pair in metadata.Custom)
            {
                fields.Add(pair);
            }

            fields.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));

            return fields;
        }

        private static long ToUnixMilliseconds(DateTimeOffset value)
        {
            return (long)(value.ToUniversalTime() - UnixEpoch).TotalMilliseconds;
        }

        private static DateTimeOffset FromUnixMilliseconds(long value)
        {
            return UnixEpoch.AddMilliseconds(value);
        }
    }
}
