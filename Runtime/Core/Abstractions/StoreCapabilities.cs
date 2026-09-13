using System;

namespace NexusChaser.EternalDPS.Abstractions
{
    /// <summary>
    /// What a given store can actually do.
    /// </summary>
    /// <remarks>
    /// Atomicity is a capability, not a promise. A file on a desktop disk can be replaced atomically
    /// by writing a temporary file and renaming it; a browser's IndexedDB cannot, and neither can
    /// the Steam Cloud API. Declaring the difference lets the driver pick a safe write strategy per
    /// store instead of assuming the best case and losing a save on the platform where the
    /// assumption was wrong.
    /// </remarks>
    [Flags]
    public enum StoreCapabilities
    {
        /// <summary>Read and write, and nothing else.</summary>
        None = 0,

        /// <summary>
        /// A write either lands whole or does not land at all. Where this is missing the driver has
        /// to make a copy first, because an interrupted write can leave a half-file behind.
        /// </summary>
        AtomicReplace = 1 << 0,

        /// <summary>
        /// The store can enumerate what it holds. Without it, the slot catalogue cannot be rebuilt
        /// by scanning and has to be trusted as written.
        /// </summary>
        List = 1 << 1,

        /// <summary>The store can remove a record.</summary>
        Delete = 1 << 2,

        /// <summary>
        /// The store can read part of a record without reading all of it. This is what makes
        /// listing slots cheap: read the preamble and the metadata, stop before the body.
        /// </summary>
        RandomAccess = 1 << 3,

        /// <summary>Everything. What a plain filesystem offers.</summary>
        All = AtomicReplace | List | Delete | RandomAccess,
    }

    /// <summary>Helpers for reading a capability set.</summary>
    public static class StoreCapabilitiesExtensions
    {
        /// <summary>True when every capability in <paramref name="required"/> is present.</summary>
        public static bool Supports(this StoreCapabilities capabilities, StoreCapabilities required)
        {
            return (capabilities & required) == required;
        }

        /// <summary>
        /// Throws when a capability the caller depends on is missing, naming it. Better than a
        /// <c>NotSupportedException</c> from three layers down with nothing in it.
        /// </summary>
        /// <exception cref="NotSupportedException">The capability is missing.</exception>
        public static void Require(this StoreCapabilities capabilities, StoreCapabilities required, string operation)
        {
            if (capabilities.Supports(required))
            {
                return;
            }

            throw new NotSupportedException(
                operation + " needs " + (required & ~capabilities) + ", which this store does not provide.");
        }
    }
}
