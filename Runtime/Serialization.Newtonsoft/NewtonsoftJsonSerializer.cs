using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using NexusChaser.EternalDPS.Abstractions;

namespace NexusChaser.EternalDPS.Serialization
{
    /// <summary>
    /// JSON through Newtonsoft, serializer <c>0x01</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default format, and the one the reserved binary serializer will eventually sit beside
    /// rather than replace. Because the container records which serializer wrote a body, a build
    /// that starts writing binary keeps reading the JSON it wrote last month, for as long as this
    /// adapter is still compiled in.
    /// </para>
    /// <para>
    /// It also implements <see cref="IDocumentSerializer"/>, which is what lets a tool inspect and
    /// edit a save without knowing the game's types.
    /// </para>
    /// </remarks>
    public sealed class NewtonsoftJsonSerializer : IDocumentSerializer
    {
        private readonly JsonSerializerSettings _settings;
        private readonly JsonSerializer _serializer;

        /// <summary>Creates the adapter with the settings the package fixes.</summary>
        /// <param name="configure">
        /// Optional. Called after the safe defaults are applied, so a game can add its own
        /// converters. The settings this package cares about are re-applied afterwards and cannot
        /// be overridden — see <see cref="CreateSettings"/> for why.
        /// </param>
        public NewtonsoftJsonSerializer(Action<JsonSerializerSettings> configure = null)
        {
            _settings = CreateSettings(configure);
            _serializer = JsonSerializer.Create(_settings);
        }

        /// <inheritdoc />
        public byte FormatId => EternalIds.SerializerJson;

        /// <summary>The settings in use. Handy for a tool that wants to match them exactly.</summary>
        public JsonSerializerSettings Settings => _settings;

        /// <summary>
        /// The settings every save is written with.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong><see cref="TypeNameHandling"/> stays off.</strong> It is a known deserialization
        /// hole: with it on, the file itself names the .NET type to construct, so anyone who can
        /// edit a save can ask the game to instantiate a type of their choosing. That turns a save
        /// file from data into a small program. The safe way to handle polymorphism is an explicit
        /// discriminator the game controls — see <see cref="EternalTypeDiscriminatorConverter{T}"/>.
        /// </para>
        /// <para>
        /// <strong>The culture is invariant.</strong> Otherwise a machine with a comma for a decimal
        /// separator writes <c>1,5</c> and every other machine reads it as something else, or
        /// refuses it. This is the classic bug that only appears once the game leaves the country
        /// it was built in.
        /// </para>
        /// <para>
        /// <strong>Dates are ISO-8601, and a <see cref="DateTime"/> is normalised to UTC.</strong>
        /// A bare local timestamp is ambiguous the moment the save crosses a time zone, and the
        /// timestamp is what decides which of two copies is newer — deciding it wrongly costs the
        /// player whichever copy was actually newer.
        /// </para>
        /// <para>
        /// A <see cref="DateTimeOffset"/> is written with the offset it already carries, and is left
        /// that way deliberately. It is not ambiguous — the offset is part of the value — so the
        /// instant survives regardless, and flattening it would throw away where the player was
        /// without buying anything. Two saves written in different zones will therefore show
        /// different text for the same instant; compare them as instants, never as strings.
        /// </para>
        /// </remarks>
        public static JsonSerializerSettings CreateSettings(Action<JsonSerializerSettings> configure = null)
        {
            var settings = new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver(),
                Formatting = Formatting.None,
                NullValueHandling = NullValueHandling.Include,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                ReferenceLoopHandling = ReferenceLoopHandling.Error,
            };

            configure?.Invoke(settings);

            // Re-applied after the game's own configuration, because these are not preferences.
            // Letting a project turn TypeNameHandling back on would reopen the hole for everyone
            // who reads that project's saves, including this package's own tools.
            settings.TypeNameHandling = TypeNameHandling.None;
            settings.MetadataPropertyHandling = MetadataPropertyHandling.Ignore;
            settings.Culture = CultureInfo.InvariantCulture;
            settings.DateFormatHandling = DateFormatHandling.IsoDateFormat;
            settings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
            settings.DateParseHandling = DateParseHandling.DateTimeOffset;
            settings.FloatParseHandling = FloatParseHandling.Double;
            settings.StringEscapeHandling = StringEscapeHandling.Default;

            return settings;
        }

        /// <inheritdoc />
        public byte[] Serialize(object value, Type type)
        {
            var json = JsonConvert.SerializeObject(value, type, _settings);

            // UTF-8 with no byte order mark. A BOM inside a container would be four bytes of noise
            // that every other reader of this format would have to know to skip.
            return new UTF8Encoding(false).GetBytes(json);
        }

        /// <inheritdoc />
        public object Deserialize(byte[] data, Type type)
        {
            if (data == null || data.Length == 0)
            {
                return type != null && type.IsValueType ? Activator.CreateInstance(type) : null;
            }

            return JsonConvert.DeserializeObject(Decode(data), type, _settings);
        }

        /// <inheritdoc />
        public EternalNode ToDocument(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return EternalNode.Null();
            }

            using (var reader = new JsonTextReader(new System.IO.StringReader(Decode(data))))
            {
                // Dates stay as written. A tool that opens a save and closes it again must not
                // rewrite a timestamp just because it recognised one.
                reader.DateParseHandling = DateParseHandling.None;
                reader.FloatParseHandling = FloatParseHandling.Double;

                return ToNode(JToken.ReadFrom(reader));
            }
        }

        /// <inheritdoc />
        public byte[] FromDocument(EternalNode document)
        {
            if (document == null)
            {
                return new byte[0];
            }

            var json = ToToken(document).ToString(Formatting.None);

            return new UTF8Encoding(false).GetBytes(json);
        }

        private static string Decode(byte[] data)
        {
            // Tolerate a byte order mark on the way in even though we never write one: a save that
            // has been through a text editor may have acquired one, and refusing to read it would
            // turn a cosmetic problem into a lost save.
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            {
                return Encoding.UTF8.GetString(data, 3, data.Length - 3);
            }

            return Encoding.UTF8.GetString(data);
        }

        private static EternalNode ToNode(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Object:
                    var objectNode = EternalNode.Object();

                    foreach (var property in (JObject)token)
                    {
                        objectNode[property.Key] = ToNode(property.Value);
                    }

                    return objectNode;

                case JTokenType.Array:
                    var arrayNode = EternalNode.Array();

                    foreach (var item in (JArray)token)
                    {
                        arrayNode.Items.Add(ToNode(item));
                    }

                    return arrayNode;

                case JTokenType.Integer:
                case JTokenType.Float:
                    // Kept as the text that was in the file. Routing a 64-bit identifier through a
                    // double loses it, and reopening a save in a tool would silently change it.
                    return EternalNode.RawNumberNode(((JValue)token).Value is double
                        ? ((double)((JValue)token).Value).ToString("R", CultureInfo.InvariantCulture)
                        : Convert.ToString(((JValue)token).Value, CultureInfo.InvariantCulture));

                case JTokenType.Boolean:
                    return EternalNode.Boolean(token.Value<bool>());

                case JTokenType.Null:
                case JTokenType.Undefined:
                    return EternalNode.Null();

                default:
                    return EternalNode.String(token.Value<string>() ?? string.Empty);
            }
        }

        private static JToken ToToken(EternalNode node)
        {
            switch (node.Kind)
            {
                case EternalNodeKind.Object:
                    var jsonObject = new JObject();

                    foreach (var member in node.Members)
                    {
                        jsonObject[member.Key] = ToToken(member.Value);
                    }

                    return jsonObject;

                case EternalNodeKind.Array:
                    var jsonArray = new JArray();

                    foreach (var item in node.Items)
                    {
                        jsonArray.Add(ToToken(item));
                    }

                    return jsonArray;

                case EternalNodeKind.Number:
                    // Parsed back from the original text, so the value written is the value read.
                    if (long.TryParse(node.RawNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
                    {
                        return new JValue(whole);
                    }

                    return new JValue(double.Parse(node.RawNumber, NumberStyles.Float, CultureInfo.InvariantCulture));

                case EternalNodeKind.Boolean:
                    return new JValue(node.BooleanValue);

                case EternalNodeKind.String:
                    return new JValue(node.StringValue);

                default:
                    return JValue.CreateNull();
            }
        }
    }
}
