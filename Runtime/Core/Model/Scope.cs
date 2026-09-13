namespace NexusChaser.EternalDPS
{
    /// <summary>
    /// Who a record belongs to. This is one of the two independent axes of the system; the other
    /// one is <see cref="RecordKind"/>.
    /// </summary>
    /// <remarks>
    /// Scope decides <em>where</em> a record lives on disk, and therefore what happens to it when
    /// something else is deleted. The rule that falls out of it, and that is broken constantly in
    /// hand-rolled save systems: deleting a slot must not delete account progress. If account
    /// progress lives inside the slot file, that bug is unavoidable.
    /// </remarks>
    public enum Scope
    {
        /// <summary>
        /// Bound to the physical device: graphics quality, resolution, audio output device.
        /// Never synchronised to the cloud, because the next machine is a different machine.
        /// </summary>
        Machine = 0,

        /// <summary>
        /// Bound to the player across devices: language, volume, accessibility, global unlocks.
        /// Survives deleting every slot.
        /// </summary>
        Account = 1,

        /// <summary>
        /// Bound to a single playthrough, identified by a <see cref="SlotId"/>. A game with one
        /// implicit save uses <see cref="SlotId.Default"/> and never notices slots exist.
        /// </summary>
        Slot = 2,
    }
}
