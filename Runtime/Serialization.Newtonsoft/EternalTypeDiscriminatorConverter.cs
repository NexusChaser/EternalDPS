using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NexusChaser.EternalDPS.Serialization
{
    /// <summary>
    /// Serialises a family of subtypes behind an explicit discriminator the game controls.
    /// </summary>
    /// <typeparam name="TBase">The base type the family shares.</typeparam>
    /// <remarks>
    /// <para>
    /// This exists because <see cref="TypeNameHandling"/> is not an option. With it on, the save
    /// file itself names the .NET type to construct, so anybody who can edit a save can name a type
    /// of their choosing and have the game build it — a save file stops being data and becomes a
    /// small program. It has produced real remote code execution in real applications.
    /// </para>
    /// <para>
    /// The fix is not to hide the mechanism but to invert who is in charge of it. The game declares
    /// a closed list of names it is willing to construct; the file may only pick from that list.
    /// A name that is not on it is refused, and no amount of editing adds one.
    /// </para>
    /// <para>
    /// The names are also a contract in their own right. They are written into saves, so renaming a
    /// C# class must not change one — which is the second reason not to use type names.
    /// </para>
    /// <example>
    /// <code>
    /// var converter = new EternalTypeDiscriminatorConverter&lt;Reward&gt;("kind")
    ///     .Register&lt;CoinReward&gt;("coins")
    ///     .Register&lt;SkinReward&gt;("skin");
    ///
    /// var serializer = new NewtonsoftJsonSerializer(s =&gt; s.Converters.Add(converter));
    /// </code>
    /// </example>
    /// </remarks>
    public sealed class EternalTypeDiscriminatorConverter<TBase> : JsonConverter
    {
        private readonly Dictionary<string, Type> _byName = new Dictionary<string, Type>(StringComparer.Ordinal);
        private readonly Dictionary<Type, string> _byType = new Dictionary<Type, string>();
        private readonly string _propertyName;

        /// <summary>Creates a converter.</summary>
        /// <param name="propertyName">
        /// The JSON property holding the discriminator. Part of the file format from the moment a
        /// save is written, so it does not change afterwards.
        /// </param>
        public EternalTypeDiscriminatorConverter(string propertyName = "$kind")
        {
            if (string.IsNullOrEmpty(propertyName))
            {
                throw new ArgumentException("The discriminator needs a property name.", nameof(propertyName));
            }

            _propertyName = propertyName;
        }

        /// <summary>The property holding the discriminator.</summary>
        public string PropertyName => _propertyName;

        /// <summary>The names this converter is willing to construct.</summary>
        public IEnumerable<string> RegisteredNames => _byName.Keys;

        /// <summary>
        /// Adds a subtype under a stable name.
        /// </summary>
        /// <remarks>
        /// The name goes into save files, so it is chosen once and never changed — renaming the C#
        /// class later must leave it alone, which is exactly the decoupling this buys.
        /// </remarks>
        /// <exception cref="ArgumentException">The name or the type is already registered.</exception>
        public EternalTypeDiscriminatorConverter<TBase> Register<TDerived>(string name) where TDerived : TBase
        {
            return Register(typeof(TDerived), name);
        }

        /// <summary>Adds a subtype under a stable name.</summary>
        /// <exception cref="ArgumentException">The name or the type is already registered.</exception>
        public EternalTypeDiscriminatorConverter<TBase> Register(Type type, string name)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A subtype needs a name.", nameof(name));
            }

            if (!typeof(TBase).IsAssignableFrom(type))
            {
                throw new ArgumentException(
                    type.Name + " does not derive from " + typeof(TBase).Name + ".", nameof(type));
            }

            if (_byName.TryGetValue(name, out var existing) && existing != type)
            {
                throw new ArgumentException(
                    "The name '" + name + "' is already registered to " + existing.Name +
                    ". A discriminator name means one type forever, because it is written into saves.",
                    nameof(name));
            }

            if (_byType.TryGetValue(type, out var existingName) && existingName != name)
            {
                throw new ArgumentException(
                    type.Name + " is already registered as '" + existingName + "'.", nameof(type));
            }

            _byName[name] = type;
            _byType[type] = name;

            return this;
        }

        /// <inheritdoc />
        public override bool CanConvert(Type objectType)
        {
            return typeof(TBase).IsAssignableFrom(objectType);
        }

        /// <inheritdoc />
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var type = value.GetType();

            if (!_byType.TryGetValue(type, out var name))
            {
                // Silently writing it without a discriminator would produce a save that cannot be
                // read back, and the failure would surface as data loss rather than as this.
                throw new JsonSerializationException(
                    type.Name + " is not registered with this converter, so it has no name to write. " +
                    "Register it before saving anything that contains one.");
            }

            var token = JObject.FromObject(value, BuildInnerSerializer(serializer));
            token[_propertyName] = name;

            token.WriteTo(writer);
        }

        /// <inheritdoc />
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var token = JToken.ReadFrom(reader);

            if (token.Type == JTokenType.Null)
            {
                return null;
            }

            if (!(token is JObject jsonObject))
            {
                throw new JsonSerializationException(
                    "Expected an object carrying a '" + _propertyName + "' discriminator.");
            }

            var discriminator = jsonObject[_propertyName];

            if (discriminator == null || discriminator.Type == JTokenType.Null)
            {
                throw new JsonSerializationException(
                    "The object has no '" + _propertyName + "' discriminator, so there is no way to " +
                    "know which type to build.");
            }

            var name = discriminator.Value<string>();

            if (!_byName.TryGetValue(name, out var type))
            {
                // The closed list is the whole point. A file asking for something not on it is
                // either from a newer build or an attempt to have us construct something arbitrary,
                // and both get the same answer.
                throw new JsonSerializationException(
                    "'" + name + "' is not a registered subtype of " + typeof(TBase).Name +
                    ". Only names the game registered can be constructed.");
            }

            // Removed before binding so that a target type with a catch-all member does not end up
            // storing the discriminator as data.
            jsonObject.Remove(_propertyName);

            var instance = Activator.CreateInstance(type);

            using (var objectReader = jsonObject.CreateReader())
            {
                BuildInnerSerializer(serializer).Populate(objectReader, instance);
            }

            return instance;
        }

        /// <summary>
        /// A serializer with this converter removed, so that reading or writing the object's own
        /// members does not come straight back here and recurse forever.
        /// </summary>
        private JsonSerializer BuildInnerSerializer(JsonSerializer outer)
        {
            var inner = new JsonSerializer
            {
                ContractResolver = outer.ContractResolver,
                NullValueHandling = outer.NullValueHandling,
                DefaultValueHandling = outer.DefaultValueHandling,
                MissingMemberHandling = outer.MissingMemberHandling,
                ObjectCreationHandling = outer.ObjectCreationHandling,
                Culture = outer.Culture,
                DateFormatHandling = outer.DateFormatHandling,
                DateTimeZoneHandling = outer.DateTimeZoneHandling,
                DateParseHandling = outer.DateParseHandling,
                FloatParseHandling = outer.FloatParseHandling,

                // Not copied from the outer serializer on purpose: whatever the rest of the
                // configuration says, this converter never enables type names.
                TypeNameHandling = TypeNameHandling.None,
                MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            };

            foreach (var converter in outer.Converters)
            {
                if (!ReferenceEquals(converter, this))
                {
                    inner.Converters.Add(converter);
                }
            }

            return inner;
        }
    }
}
