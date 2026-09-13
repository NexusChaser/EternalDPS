using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NexusChaser.EternalDPS.Abstractions
{
    /// <summary>
    /// Somewhere bytes can be kept under a relative path: a folder, a browser database, Steam
    /// Cloud, a console's save data API.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Every method is asynchronous, and that is not a style preference.</strong> On WebGL,
    /// Unity does not flush writes to IndexedDB on its own; the flush has to be triggered from a
    /// JavaScript plugin and it completes later. A synchronous <c>Save()</c> would return before the
    /// data had actually left memory, so on WebGL it would be wrong every single time, in a way
    /// that only shows up when the player closes the tab. Console save data APIs are asynchronous
    /// for their own reasons. Making the whole core asynchronous is the only shape that is correct
    /// on all of them.
    /// </para>
    /// <para>
    /// Paths use a forward slash as separator and are always relative to the store's own root. A
    /// store is free to map them onto whatever it really has: the Steam Cloud implementation flattens
    /// them, because <c>ISteamRemoteStorage</c> has a single flat namespace per app and user, with
    /// no folders at all.
    /// </para>
    /// <para>
    /// Implementations must never let a path escape their root. <see cref="EternalKey"/> builds
    /// safe paths, but a store can also be handed a path from a file on disk, and that one has not
    /// been through any validation.
    /// </para>
    /// </remarks>
    public interface IStore
    {
        /// <summary>What this store can do. Constant for the life of the instance.</summary>
        StoreCapabilities Capabilities { get; }

        /// <summary>Whether a record exists.</summary>
        Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken);

        /// <summary>
        /// Reads a record whole.
        /// </summary>
        /// <returns>
        /// The bytes, or <c>null</c> when there is no such record. Absence is an ordinary outcome
        /// here — a first run has no saves — so it is a return value and not an exception.
        /// </returns>
        Task<byte[]> ReadAsync(string relativePath, CancellationToken cancellationToken);

        /// <summary>
        /// Writes a record, replacing whatever was there. Creates any intermediate folders the
        /// store needs.
        /// </summary>
        /// <remarks>
        /// When the store declares <see cref="StoreCapabilities.AtomicReplace"/>, an interrupted
        /// write must leave the previous contents intact. When it does not, the driver takes its
        /// own precautions first.
        /// </remarks>
        Task WriteAsync(string relativePath, byte[] data, CancellationToken cancellationToken);

        /// <summary>
        /// Removes a record. Deleting something that is not there succeeds quietly — the end state
        /// the caller asked for is the state that already holds.
        /// </summary>
        /// <exception cref="NotSupportedException">
        /// The store does not declare <see cref="StoreCapabilities.Delete"/>.
        /// </exception>
        Task DeleteAsync(string relativePath, CancellationToken cancellationToken);

        /// <summary>
        /// Lists the records under a prefix, recursively, as relative paths in the same form
        /// <see cref="ReadAsync"/> accepts. An empty prefix lists everything.
        /// </summary>
        /// <exception cref="NotSupportedException">
        /// The store does not declare <see cref="StoreCapabilities.List"/>.
        /// </exception>
        Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken cancellationToken);

        /// <summary>
        /// Pushes anything buffered to where it actually survives. On WebGL this is the IndexedDB
        /// flush; on a plain filesystem it does nothing. Called by the driver after a write, so a
        /// caller normally never has to.
        /// </summary>
        Task FlushAsync(CancellationToken cancellationToken);
    }

    /// <summary>Overloads without an explicit cancellation token.</summary>
    public static class StoreExtensions
    {
        /// <summary>Whether a record exists.</summary>
        public static Task<bool> ExistsAsync(this IStore store, string relativePath)
        {
            return Check(store).ExistsAsync(relativePath, CancellationToken.None);
        }

        /// <summary>Reads a record whole, or null when it is not there.</summary>
        public static Task<byte[]> ReadAsync(this IStore store, string relativePath)
        {
            return Check(store).ReadAsync(relativePath, CancellationToken.None);
        }

        /// <summary>Writes a record, replacing whatever was there.</summary>
        public static Task WriteAsync(this IStore store, string relativePath, byte[] data)
        {
            return Check(store).WriteAsync(relativePath, data, CancellationToken.None);
        }

        /// <summary>Removes a record.</summary>
        public static Task DeleteAsync(this IStore store, string relativePath)
        {
            return Check(store).DeleteAsync(relativePath, CancellationToken.None);
        }

        /// <summary>Lists the records under a prefix.</summary>
        public static Task<IReadOnlyList<string>> ListAsync(this IStore store, string prefix)
        {
            return Check(store).ListAsync(prefix, CancellationToken.None);
        }

        /// <summary>Pushes anything buffered to where it survives.</summary>
        public static Task FlushAsync(this IStore store)
        {
            return Check(store).FlushAsync(CancellationToken.None);
        }

        private static IStore Check(IStore store)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            return store;
        }
    }
}
