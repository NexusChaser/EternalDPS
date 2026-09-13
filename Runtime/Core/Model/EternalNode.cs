using System;
using System.Collections.Generic;
using System.Globalization;

namespace NexusChaser.EternalDPS
{
    /// <summary>What an <see cref="EternalNode"/> holds.</summary>
    public enum EternalNodeKind
    {
        /// <summary>An explicit absence of value.</summary>
        Null = 0,

        /// <summary>True or false.</summary>
        Boolean = 1,

        /// <summary>A number, kept as written. See <see cref="EternalNode.RawNumber"/>.</summary>
        Number = 2,

        /// <summary>Text.</summary>
        String = 3,

        /// <summary>An ordered list of nodes.</summary>
        Array = 4,

        /// <summary>A set of named nodes.</summary>
        Object = 5,
    }

    /// <summary>
    /// A format-independent tree for looking at, and editing, the contents of a save without
    /// knowing the game's types.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what the editor windows and the command line bind to. It has to exist in the core,
    /// and it has to be independent of any particular serialiser, or every tool would be written
    /// twice — once for JSON and once for whatever binary format replaces it.
    /// </para>
    /// <para>
    /// Numbers keep the text they arrived as. Routing every number through <c>double</c> would
    /// quietly round a 64-bit identifier or a currency value on the way in and write a different
    /// number on the way out. Opening a save in a tool and closing it again without editing
    /// anything must produce the same bytes.
    /// </para>
    /// </remarks>
    public sealed class EternalNode
    {
        private readonly List<EternalNode> _items;
        private readonly Dictionary<string, EternalNode> _members;
        private readonly bool _boolean;
        private readonly string _text;

        private EternalNode(
            EternalNodeKind kind,
            bool boolean = false,
            string text = null,
            List<EternalNode> items = null,
            Dictionary<string, EternalNode> members = null)
        {
            Kind = kind;
            _boolean = boolean;
            _text = text;
            _items = items;
            _members = members;
        }

        /// <summary>What this node holds.</summary>
        public EternalNodeKind Kind { get; }

        /// <summary>The value of a <see cref="EternalNodeKind.Boolean"/> node.</summary>
        /// <exception cref="InvalidOperationException">This node is not a boolean.</exception>
        public bool BooleanValue
        {
            get
            {
                Require(EternalNodeKind.Boolean);
                return _boolean;
            }
        }

        /// <summary>The value of a <see cref="EternalNodeKind.String"/> node.</summary>
        /// <exception cref="InvalidOperationException">This node is not a string.</exception>
        public string StringValue
        {
            get
            {
                Require(EternalNodeKind.String);
                return _text;
            }
        }

        /// <summary>
        /// A number exactly as it was written, so that reading and rewriting it loses nothing. Use
        /// <see cref="TryGetInt64"/> or <see cref="TryGetDouble"/> to interpret it.
        /// </summary>
        /// <exception cref="InvalidOperationException">This node is not a number.</exception>
        public string RawNumber
        {
            get
            {
                Require(EternalNodeKind.Number);
                return _text;
            }
        }

        /// <summary>The elements of an <see cref="EternalNodeKind.Array"/> node.</summary>
        /// <exception cref="InvalidOperationException">This node is not an array.</exception>
        public IList<EternalNode> Items
        {
            get
            {
                Require(EternalNodeKind.Array);
                return _items;
            }
        }

        /// <summary>The members of an <see cref="EternalNodeKind.Object"/> node.</summary>
        /// <exception cref="InvalidOperationException">This node is not an object.</exception>
        public IDictionary<string, EternalNode> Members
        {
            get
            {
                Require(EternalNodeKind.Object);
                return _members;
            }
        }

        /// <summary>A member of an object node, or null when there is no such member.</summary>
        /// <exception cref="InvalidOperationException">This node is not an object.</exception>
        public EternalNode this[string name]
        {
            get
            {
                Require(EternalNodeKind.Object);
                return _members.TryGetValue(name, out var node) ? node : null;
            }
            set
            {
                Require(EternalNodeKind.Object);
                _members[name] = value ?? Null();
            }
        }

        /// <summary>An element of an array node.</summary>
        /// <exception cref="InvalidOperationException">This node is not an array.</exception>
        public EternalNode this[int index]
        {
            get
            {
                Require(EternalNodeKind.Array);
                return _items[index];
            }
            set
            {
                Require(EternalNodeKind.Array);
                _items[index] = value ?? Null();
            }
        }

        /// <summary>A null node.</summary>
        public static EternalNode Null()
        {
            return new EternalNode(EternalNodeKind.Null);
        }

        /// <summary>A boolean node.</summary>
        public static EternalNode Boolean(bool value)
        {
            return new EternalNode(EternalNodeKind.Boolean, boolean: value);
        }

        /// <summary>A string node.</summary>
        public static EternalNode String(string value)
        {
            return value == null ? Null() : new EternalNode(EternalNodeKind.String, text: value);
        }

        /// <summary>A number node from a whole number.</summary>
        public static EternalNode Number(long value)
        {
            return new EternalNode(EternalNodeKind.Number, text: value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// A number node from a floating point value, round-tripped so that reading it back gives
        /// the same bits.
        /// </summary>
        public static EternalNode Number(double value)
        {
            return new EternalNode(EternalNodeKind.Number, text: value.ToString("R", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// A number node from text produced by a parser, kept verbatim. The caller is responsible
        /// for the text being a number in the first place.
        /// </summary>
        /// <exception cref="ArgumentException">The text is null or empty.</exception>
        public static EternalNode RawNumberNode(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                throw new ArgumentException("A number node needs its original text.", nameof(raw));
            }

            return new EternalNode(EternalNodeKind.Number, text: raw);
        }

        /// <summary>An empty array node.</summary>
        public static EternalNode Array()
        {
            return new EternalNode(EternalNodeKind.Array, items: new List<EternalNode>());
        }

        /// <summary>
        /// An empty object node. Member names are compared with ordinal case sensitivity, never
        /// with the current culture — a save written on a Turkish machine has to name the same
        /// members as one written anywhere else.
        /// </summary>
        /// <remarks>
        /// Member order is not preserved. That is fine for loading, and it is worth revisiting when
        /// the tooling layer lands, because a tool that rewrites a file and reshuffles its members
        /// produces a diff nobody can read.
        /// </remarks>
        public static EternalNode Object()
        {
            return new EternalNode(EternalNodeKind.Object, members: new Dictionary<string, EternalNode>(StringComparer.Ordinal));
        }

        /// <summary>Reads the number as a 64-bit integer.</summary>
        public bool TryGetInt64(out long value)
        {
            value = 0;
            return Kind == EternalNodeKind.Number
                   && long.TryParse(_text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>Reads the number as a double.</summary>
        public bool TryGetDouble(out double value)
        {
            value = 0;
            return Kind == EternalNodeKind.Number
                   && double.TryParse(_text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private void Require(EternalNodeKind expected)
        {
            if (Kind != expected)
            {
                throw new InvalidOperationException("This node is " + Kind + ", not " + expected + ".");
            }
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case EternalNodeKind.Null:
                    return "null";
                case EternalNodeKind.Boolean:
                    return _boolean ? "true" : "false";
                case EternalNodeKind.Number:
                    return _text;
                case EternalNodeKind.String:
                    return "\"" + _text + "\"";
                case EternalNodeKind.Array:
                    return "[" + _items.Count + " items]";
                case EternalNodeKind.Object:
                    return "{" + _members.Count + " members}";
                default:
                    return Kind.ToString();
            }
        }
    }
}
