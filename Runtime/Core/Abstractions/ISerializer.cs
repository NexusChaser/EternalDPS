using System;

namespace NexusChaser.EternalDPS.Abstractions
{
    /// <summary>
    /// Turns an object into bytes and back. One implementation per format.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="FormatId"/> is written into the container, which is what makes swapping
    /// formats a non-event: a build that starts writing binary keeps reading the JSON files it
    /// wrote last month, as long as the JSON adapter is still compiled in. There is no conversion
    /// day and no migration pass — files move over as they are rewritten.
    /// </para>
    /// <para>
    /// The methods take a <see cref="Type"/> rather than being generic. Generic interface methods
    /// over value types are exactly what IL2CPP strips when it cannot see the instantiation, and a
    /// save system that works in the editor and fails on device is worse than no save system. The
    /// generic convenience wrappers live in <see cref="SerializerExtensions"/>, on top.
    /// </para>
    /// </remarks>
    public interface ISerializer
    {
        /// <summary>
        /// Identifies this format inside the container. Registered once and never reused for
        /// anything else: 0x01..0x7F belong to the package, 0x80..0xFF to the game.
        /// </summary>
        byte FormatId { get; }

        /// <summary>Encodes a value.</summary>
        /// <param name="value">What to encode. May be null if the format can express that.</param>
        /// <param name="type">The declared type, which is not always the runtime type.</param>
        byte[] Serialize(object value, Type type);

        /// <summary>Decodes a value.</summary>
        /// <param name="data">Bytes produced by <see cref="Serialize"/> of the same format.</param>
        /// <param name="type">The type to produce.</param>
        /// <exception cref="Exception">
        /// Implementations may throw on malformed input. The driver catches it and reports
        /// <see cref="LoadStatus.Corrupt"/> rather than letting it reach the game.
        /// </exception>
        object Deserialize(byte[] data, Type type);
    }

    /// <summary>Generic sugar over <see cref="ISerializer"/>.</summary>
    public static class SerializerExtensions
    {
        /// <summary>Encodes a value using its static type.</summary>
        public static byte[] Serialize<T>(this ISerializer serializer, T value)
        {
            if (serializer == null)
            {
                throw new ArgumentNullException(nameof(serializer));
            }

            return serializer.Serialize(value, typeof(T));
        }

        /// <summary>Decodes a value of a known type.</summary>
        public static T Deserialize<T>(this ISerializer serializer, byte[] data)
        {
            if (serializer == null)
            {
                throw new ArgumentNullException(nameof(serializer));
            }

            return (T)serializer.Deserialize(data, typeof(T));
        }
    }
}
