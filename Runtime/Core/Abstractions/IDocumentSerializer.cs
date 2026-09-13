namespace NexusChaser.EternalDPS.Abstractions
{
    /// <summary>
    /// A serialiser that can also expose a payload as an editable tree, without knowing the game's
    /// types.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Optional on purpose. A format may be able to encode and decode perfectly well and still have
    /// no meaningful tree view — a hand-rolled binary layout, for instance. Tools check for this
    /// interface and offer the editing view only when it is there.
    /// </para>
    /// <para>
    /// This is what makes an inspector window possible at all. Without it, viewing a save would
    /// mean deserialising it into the game's own classes, which only works inside the game that
    /// wrote it, and defeats the point of a reusable package.
    /// </para>
    /// </remarks>
    public interface IDocumentSerializer : ISerializer
    {
        /// <summary>Reads a payload as a tree.</summary>
        /// <param name="data">Bytes of this serialiser's format.</param>
        EternalNode ToDocument(byte[] data);

        /// <summary>
        /// Writes a tree back out. Round-tripping an untouched tree must produce bytes that decode
        /// to the same values.
        /// </summary>
        byte[] FromDocument(EternalNode document);
    }
}
