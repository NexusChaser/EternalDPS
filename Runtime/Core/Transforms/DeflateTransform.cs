using System;
using System.IO;
using System.IO.Compression;
using NexusChaser.EternalDPS.Abstractions;
using NexusChaser.EternalDPS.Container;

namespace NexusChaser.EternalDPS.Transforms
{
    /// <summary>
    /// Deflate compression, transform <c>0x01</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Part of the default profile together with the signature. It earns its place twice: a save
    /// that went through it is binary noise in a text editor, so renaming the file gets a curious
    /// player nowhere, and it is smaller, which matters against a cloud quota measured in bytes.
    /// </para>
    /// <para>
    /// It is not protection and is not claimed to be. Deflate is a published format and anyone who
    /// wants to decompress it can. What stops a save being edited is the signature.
    /// </para>
    /// </remarks>
    public sealed class DeflateTransform : IByteTransform
    {
        /// <inheritdoc />
        public byte TransformId => EternalIds.TransformDeflate;

        /// <inheritdoc />
        public byte[] Apply(byte[] input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            using (var output = new MemoryStream())
            {
                // The stream is closed before the buffer is read: DeflateStream writes its final
                // block on dispose, so reading ToArray() while it is still open loses the tail.
                using (var deflate = new DeflateStream(output, CompressionMode.Compress, leaveOpen: true))
                {
                    deflate.Write(input, 0, input.Length);
                }

                return output.ToArray();
            }
        }

        /// <inheritdoc />
        /// <exception cref="EternalFormatException">The input is not valid Deflate data.</exception>
        public byte[] Invert(byte[] input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            try
            {
                using (var source = new MemoryStream(input, writable: false))
                using (var deflate = new DeflateStream(source, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    deflate.CopyTo(output);
                    return output.ToArray();
                }
            }
            catch (InvalidDataException error)
            {
                // Reached when a file passed verification and still will not decompress, which
                // means the bytes are exactly what was written and we wrote something wrong.
                throw new EternalFormatException("The body is not valid Deflate data.", error);
            }
        }
    }
}
