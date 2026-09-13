using System;
using System.Text;

namespace NexusChaser.EternalDPS.Container
{
    /// <summary>
    /// Fixed-endianness readers and writers for the container format.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything is <strong>little endian, written by hand</strong>. <c>BitConverter</c> follows
    /// whatever the host processor does, so a file written on one architecture would not be
    /// readable on another — and the whole point of this format is that a save travels between
    /// machines. The cost of doing the shifts explicitly is a few lines; the cost of not doing it
    /// only appears on hardware nobody on the team owns.
    /// </para>
    /// <para>
    /// GUIDs are written in RFC 4122 byte order rather than <see cref="Guid.ToByteArray"/>'s, which
    /// reverses the first three fields. Both round-trip inside .NET, but only one of them means the
    /// same thing to a reader written in another language — and the product id in a save file is
    /// exactly the kind of thing an external tool will want to read one day.
    /// </para>
    /// </remarks>
    internal static class Bytes
    {
        /// <summary>Longest string this format will write or accept in one field.</summary>
        internal const int MaxStringLength = ushort.MaxValue;

        internal static void WriteUInt16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        internal static ushort ReadUInt16(byte[] buffer, int offset)
        {
            return (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
        }

        internal static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        internal static uint ReadUInt32(byte[] buffer, int offset)
        {
            return (uint)(buffer[offset]
                          | (buffer[offset + 1] << 8)
                          | (buffer[offset + 2] << 16)
                          | (buffer[offset + 3] << 24));
        }

        internal static void WriteInt64(byte[] buffer, int offset, long value)
        {
            var unsigned = unchecked((ulong)value);
            for (var i = 0; i < 8; i++)
            {
                buffer[offset + i] = (byte)(unsigned >> (i * 8));
            }
        }

        internal static long ReadInt64(byte[] buffer, int offset)
        {
            ulong unsigned = 0;
            for (var i = 0; i < 8; i++)
            {
                unsigned |= (ulong)buffer[offset + i] << (i * 8);
            }

            return unchecked((long)unsigned);
        }

        /// <summary>Writes a GUID as the 16 bytes of RFC 4122, big endian in its first three fields.</summary>
        internal static void WriteGuid(byte[] buffer, int offset, Guid value)
        {
            var raw = value.ToByteArray();

            // .NET stores Data1 (4 bytes), Data2 and Data3 (2 bytes each) little endian. RFC 4122
            // puts them big endian. The remaining 8 bytes are already in order in both.
            buffer[offset + 0] = raw[3];
            buffer[offset + 1] = raw[2];
            buffer[offset + 2] = raw[1];
            buffer[offset + 3] = raw[0];
            buffer[offset + 4] = raw[5];
            buffer[offset + 5] = raw[4];
            buffer[offset + 6] = raw[7];
            buffer[offset + 7] = raw[6];

            Array.Copy(raw, 8, buffer, offset + 8, 8);
        }

        /// <summary>Reads a GUID written by <see cref="WriteGuid"/>.</summary>
        internal static Guid ReadGuid(byte[] buffer, int offset)
        {
            var raw = new byte[16];

            raw[0] = buffer[offset + 3];
            raw[1] = buffer[offset + 2];
            raw[2] = buffer[offset + 1];
            raw[3] = buffer[offset + 0];
            raw[4] = buffer[offset + 5];
            raw[5] = buffer[offset + 4];
            raw[6] = buffer[offset + 7];
            raw[7] = buffer[offset + 6];

            Array.Copy(buffer, offset + 8, raw, 8, 8);

            return new Guid(raw);
        }

        /// <summary>
        /// Number of bytes <see cref="WriteString"/> will use: a two byte length plus the UTF-8
        /// encoding of the text.
        /// </summary>
        internal static int MeasureString(string value)
        {
            return 2 + (string.IsNullOrEmpty(value) ? 0 : Encoding.UTF8.GetByteCount(value));
        }

        /// <summary>Writes a length-prefixed UTF-8 string and returns the offset past it.</summary>
        /// <exception cref="EternalFormatException">The encoded text does not fit in the length prefix.</exception>
        internal static int WriteString(byte[] buffer, int offset, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                WriteUInt16(buffer, offset, 0);
                return offset + 2;
            }

            var count = Encoding.UTF8.GetByteCount(value);

            if (count > MaxStringLength)
            {
                throw new EternalFormatException(
                    "A text field of " + count + " bytes does not fit in this format, which allows " +
                    MaxStringLength + ".");
            }

            WriteUInt16(buffer, offset, (ushort)count);
            Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, offset + 2);

            return offset + 2 + count;
        }

        /// <summary>
        /// Reads a length-prefixed UTF-8 string, refusing to read past <paramref name="limit"/>.
        /// </summary>
        /// <remarks>
        /// The bound matters: the length prefix comes from the file, and a corrupted or hostile one
        /// is exactly how a reader is talked into walking off the end of the buffer.
        /// </remarks>
        /// <exception cref="EternalFormatException">The field runs past the end of the region.</exception>
        internal static int ReadString(byte[] buffer, int offset, int limit, out string value)
        {
            Require(offset, 2, limit, "a text length");

            var count = ReadUInt16(buffer, offset);
            Require(offset + 2, count, limit, "a text field");

            value = count == 0 ? string.Empty : Encoding.UTF8.GetString(buffer, offset + 2, count);

            return offset + 2 + count;
        }

        /// <summary>Fails when a read of <paramref name="length"/> bytes would run past the region.</summary>
        /// <exception cref="EternalFormatException">The read does not fit.</exception>
        internal static void Require(int offset, int length, int limit, string what)
        {
            if (offset < 0 || length < 0 || offset + length > limit)
            {
                throw new EternalFormatException(
                    "Reading " + what + " would run past the end of the region: needed " + length +
                    " bytes at offset " + offset + ", region ends at " + limit + ".");
            }
        }

        /// <summary>
        /// Compares two byte spans in time that does not depend on where they first differ.
        /// </summary>
        /// <remarks>
        /// Used for the signature. A comparison that returns as soon as it finds a difference leaks,
        /// through how long it took, how many leading bytes were correct — which is enough to forge
        /// a signature one byte at a time. It costs nothing to avoid, so there is no reason not to.
        /// </remarks>
        internal static bool ConstantTimeEquals(byte[] a, int aOffset, byte[] b, int bOffset, int length)
        {
            var difference = 0;

            for (var i = 0; i < length; i++)
            {
                difference |= a[aOffset + i] ^ b[bOffset + i];
            }

            return difference == 0;
        }
    }
}
