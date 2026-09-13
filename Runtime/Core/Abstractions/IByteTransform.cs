namespace NexusChaser.EternalDPS.Abstractions
{
    /// <summary>
    /// A reversible step applied to the payload on the way out and undone on the way in:
    /// compression, encryption, anything else that is bytes in and bytes out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The container stores an <em>ordered list</em> of transform ids, not a set of flags. The file
    /// carries the recipe for undoing itself, which is what makes a change forward compatible:
    /// files written before encryption was added keep loading after it is added, because they never
    /// claimed to be encrypted.
    /// </para>
    /// <para>
    /// That list is covered by the signature. Without that, stripping the encryption id from a file
    /// would be enough to walk past the protection entirely.
    /// </para>
    /// </remarks>
    public interface IByteTransform
    {
        /// <summary>
        /// Identifies this transform inside the container. Registered once and never reused:
        /// 0x01..0x7F belong to the package, 0x80..0xFF to the game.
        /// </summary>
        byte TransformId { get; }

        /// <summary>Applies the transform, on the way to disk.</summary>
        byte[] Apply(byte[] input);

        /// <summary>
        /// Undoes the transform, on the way back. <c>Invert(Apply(x))</c> must equal <c>x</c> for
        /// every input, including an empty array.
        /// </summary>
        byte[] Invert(byte[] input);
    }
}
