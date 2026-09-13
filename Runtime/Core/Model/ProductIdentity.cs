using System;
using System.Collections.Generic;

namespace NexusChaser.EternalDPS
{
    /// <summary>
    /// Which game a save belongs to, independently of what the game is currently called.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Save folders are built from the company and product names. Rename either — which happens
    /// near the end of almost every project — and the engine starts looking in a new, empty folder
    /// while the player's saves sit in the old one, orphaned. Shipping a build that does that to
    /// people is not acceptable.
    /// </para>
    /// <para>
    /// The fix is to invert where identity lives: <strong>inside the file, not in the path</strong>.
    /// A frozen GUID in the metadata says whose the save is, so a rename stops being an event.
    /// </para>
    /// <para>
    /// <see cref="Inherited"/> is for identities the game used to write under. It is how a project
    /// that already shipped before adopting this system, or one that merged with another, keeps
    /// reading what it wrote.
    /// </para>
    /// </remarks>
    public sealed class ProductIdentity
    {
        private readonly Guid[] _inherited;

        /// <summary>Declares the current identity and any it also accepts.</summary>
        /// <param name="current">
        /// Generated <strong>once</strong> and frozen for the life of the product. Not derived from
        /// the name, not regenerated per build, not changed when the game is renamed.
        /// </param>
        /// <param name="inherited">Identities this game used to write under, if any.</param>
        /// <exception cref="ArgumentException">The current identity is empty.</exception>
        public ProductIdentity(Guid current, params Guid[] inherited)
        {
            if (current == Guid.Empty)
            {
                throw new ArgumentException(
                    "The empty GUID is not a product identity. Generate one once and freeze it.",
                    nameof(current));
            }

            Current = current;
            _inherited = inherited ?? new Guid[0];
        }

        /// <summary>The identity new saves are written with.</summary>
        public Guid Current { get; }

        /// <summary>Identities still accepted when reading.</summary>
        public IReadOnlyList<Guid> Inherited => _inherited;

        /// <summary>True when a save carrying this identity is ours.</summary>
        public bool Accepts(Guid productId)
        {
            if (productId == Current)
            {
                return true;
            }

            for (var i = 0; i < _inherited.Length; i++)
            {
                if (_inherited[i] == productId)
                {
                    return true;
                }
            }

            return false;
        }

        public override string ToString()
        {
            return _inherited.Length == 0
                ? Current.ToString()
                : Current + " (+" + _inherited.Length + " inherited)";
        }
    }
}
