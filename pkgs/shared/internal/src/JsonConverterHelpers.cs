using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LdJson = LaunchDarkly.Sdk.Json;

namespace LaunchDarkly.Sdk.Internal
{
    /// <summary>
    /// Helper methods for using the System.Text.Json API more conveniently in custom converters.
    /// </summary>
    public static class JsonConverterHelpers
    {
        /// <summary>
        /// Throws an exception if the current JSON token is not of the expected type.
        /// </summary>
        /// <param name="reader">the JSON reader</param>
        /// <param name="expectedType">the expected token type</param>
        /// <exception cref="JsonException">if the token type is wrong</exception>
        public static void RequireTokenType(ref Utf8JsonReader reader, JsonTokenType expectedType)
        {
            if (reader.TokenType != expectedType)
            {
                throw new JsonException("Expected " + expectedType + ", got " + reader.TokenType);
            }
        }

        /// <summary>
        /// Reads either a numeric value or null.
        /// </summary>
        /// <param name="reader">the JSON reader</param>
        /// <returns>a nullable int</returns>
        /// <exception cref="InvalidOperationException">if the token type is neither null or number</exception>
        public static int? GetIntOrNull(ref Utf8JsonReader reader)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                reader.Skip();
                return null;
            }
            return reader.GetInt32();
        }

        /// <summary>
        /// Reads either a numeric value or null.
        /// </summary>
        /// <param name="reader">the JSON reader</param>
        /// <returns>a nullable long</returns>
        /// <exception cref="InvalidOperationException">if the token type is neither null or number</exception>
        public static long? GetLongOrNull(ref Utf8JsonReader reader)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                reader.Skip();
                return null;
            }
            return reader.GetInt64();
        }

        /// <summary>
        /// Reads either a millisecond timestamp value or null.
        /// </summary>
        /// <param name="reader">the JSON reader</param>
        /// <returns>a nullable timestamp</returns>
        /// <exception cref="InvalidOperationException">if the token type is neither null or number</exception>
        public static UnixMillisecondTime? GetTimeOrNull(ref Utf8JsonReader reader)
        {
            var n = GetLongOrNull(ref reader);
            return n.HasValue ? UnixMillisecondTime.OfMillis(n.Value) : (UnixMillisecondTime?)null;
        }

        /// <summary>
        /// Shortcut for creating a JSON writer, performing some actions on it, and then getting its output as a
        /// string.
        /// </summary>
        /// <param name="action">the action to do with the JSON writer</param>
        /// <returns>the string output</returns>
        public static string WriteJsonAsString(Action<Utf8JsonWriter> action)
        {
            var stream = new MemoryStream();
            var w = new Utf8JsonWriter(stream);
            action(w);
            w.Flush();
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        /// <summary>
        /// Writes a numeric property value if it is not null; if null, omits the property entirely.
        /// </summary>
        /// <param name="w">the JSON writer</param>
        /// <param name="name">the property name</param>
        /// <param name="value">a nullable int</param>
        public static void WriteIntIfNotNull(Utf8JsonWriter w, string name, int? value)
        {
            if (value.HasValue)
            {
                w.WriteNumber(name, value.Value);
            }
        }

        /// <summary>
        /// Writes a numeric <see cref="UnixMillisecondTime"/> value if it is not null; if null, omits the
        /// property entirely.
        /// </summary>
        /// <param name="w">the JSON writer</param>
        /// <param name="name">the property name</param>
        /// <param name="value">a nullable timestamp</param>
        public static void WriteTimeIfNotNull(Utf8JsonWriter w, string name, UnixMillisecondTime? value)
        {
            if (value.HasValue)
            {
                w.WriteNumber(name, value.Value.Value);
            }
        }

        /// <summary>
        /// Writes a string property value if it is not null; if null, omits the property entirely.
        /// </summary>
        /// <param name="w">the JSON writer</param>
        /// <param name="name">the property name</param>
        /// <param name="value">a nullable string</param>
        public static void WriteStringIfNotNull(Utf8JsonWriter w, string name, string value)
        {
            if (!(value is null))
            {
                w.WriteString(name, value);
            }
        }

        /// <summary>
        /// Writes a boolean property value if it is true; if false, omits the property entirely.
        /// </summary>
        /// <param name="w">the JSON writer</param>
        /// <param name="name">the property name</param>
        /// <param name="value">a boolean</param>
        public static void WriteBooleanIfTrue(Utf8JsonWriter w, string name, bool value)
        {
            if (value)
            {
                w.WriteBoolean(name, true);
            }
        }

        /// <summary>
        /// Writes an <see cref="LdValue"/> as a property value.
        /// </summary>
        /// <param name="w">the JSON writer</param>
        /// <param name="name">the property name</param>
        /// <param name="value">the value</param>
        public static void WriteLdValue(Utf8JsonWriter w, string name, LdValue value)
        {
            w.WritePropertyName(name);
            LdJson.LdJsonConverters.LdValueConverter.WriteJsonValue(value, w);
        }

        /// <summary>
        /// Writes an <see cref="LdValue"/> as a property value if it is not <see cref="LdValue.Null"/>;
        /// if it is, omits the property entirely.
        /// </summary>
        /// <param name="w">the JSON writer</param>
        /// <param name="name">the property name</param>
        /// <param name="value">the value</param>
        public static void WriteLdValueIfNotNull(Utf8JsonWriter w, string name, LdValue value)
        {
            if (!value.IsNull)
            {
                WriteLdValue(w, name, value);
            }
        }

        /// <summary>
        /// Starts consuming a JSON array. Throws an exception if the next token is not an array.
        /// </summary>
        /// <example><code>
        ///     var arrayOfStrings = RequireArray(ref reader);
        ///     while (arrayOfStrings.Next(ref reader))
        ///     {
        ///         DoSomething(reader.GetString());
        ///     }
        /// </code></example>
        /// <param name="reader">the JSON reader</param>
        /// <returns>an <see cref="ArrayHelper"/></returns>
        /// <exception cref="JsonException">if the next token is not the beginning of an array</exception>
        public static ArrayHelper RequireArray(scoped ref Utf8JsonReader reader) =>
            new ArrayHelper(ref reader, false);

        /// <summary>
        /// Same as <see cref="RequireArray(ref Utf8JsonReader)"/>, except that if the next token is
        /// a null, it behaves the same as an empty array.
        /// </summary>
        /// <param name="reader">the JSON reader</param>
        /// <returns>an <see cref="ArrayHelper"/></returns>
        /// <exception cref="JsonException">if the next token is not the beginning of an array or null</exception>
        public static ArrayHelper RequireArrayOrNull(scoped ref Utf8JsonReader reader) =>
            new ArrayHelper(ref reader, true);

        /// <summary>
        /// Starts consuming a JSON object. Throws an exception if the next token is not an object.
        /// See <see cref="ObjectHelper"/> for usage.
        /// </summary>
        /// <param name="reader">the JSON reader</param>
        /// <returns>an <see cref="ObjectHelper"/></returns>
        /// <exception cref="JsonException">if the next token is not the beginning of an object</exception>
        public static ObjectHelper RequireObject(scoped ref Utf8JsonReader reader)
        {
            if (reader.TokenType == JsonTokenType.None)
            {
                reader.Read();
            }
            RequireTokenType(ref reader, JsonTokenType.StartObject);
            return new ObjectHelper(true, null);
        }

        /// <summary>
        /// Same as <see cref="RequireObject(ref Utf8JsonReader)"/>, except that if the next token is
        /// a null, it behaves the same as an empty object.
        /// </summary>
        /// <param name="reader">the JSON reader</param>
        /// <returns>an <see cref="ObjectHelper"/></returns>
        /// <exception cref="JsonException">if the next token is not the beginning of an object or null</exception>
        public static ObjectHelper RequireObjectOrNull(scoped ref Utf8JsonReader reader)
        {
            if (reader.TokenType == JsonTokenType.None)
            {
                reader.Read();
            }
            if (reader.TokenType == JsonTokenType.Null)
            {
                reader.Skip();
                return new ObjectHelper(false, null);
            }
            return RequireObject(ref reader);
        }

        /// <summary>
        /// A helper for reading JSON arrays.
        /// </summary>
        /// <seealso cref="RequireArray(ref Utf8JsonReader)"/>
        /// <seealso cref="RequireArrayOrNull(ref Utf8JsonReader)"/>
        public ref struct ArrayHelper
        {
            private readonly bool _empty;

            internal ArrayHelper(scoped ref Utf8JsonReader reader, bool allowNull)
            {
                if (reader.TokenType == JsonTokenType.None)
                {
                    reader.Read();
                }
                if (allowNull && reader.TokenType == JsonTokenType.Null)
                {
                    reader.Skip();
                    _empty = true;
                }
                else
                {
                    RequireTokenType(ref reader, JsonTokenType.StartArray);
                    _empty = false;
                }
            }

            /// <summary>
            /// Tries to read the next array element.
            /// </summary>
            /// <param name="reader">the JSON reader (this must be passed again due to ref struct rules)</param>
            /// <returns>true if there is an element, false if this is the end</returns>
            public bool Next(scoped ref Utf8JsonReader reader) =>
                !_empty && reader.Read() && reader.TokenType != JsonTokenType.EndArray;
        }

        /// <summary>
        /// A helper for reading JSON objects. Note that this implementation always returns the property name
        /// as a string. If you require very high efficiency and don't want to allocate strings for property
        /// names, use the lower-level <see cref="Utf8JsonReader"/> methods instead.
        /// </summary>
        /// <example><code>
        ///     for (var obj = RequireObject(ref reader); obj.Next(ref reader);)
        ///     {
        ///         DoSomething(objWithIntValues.Name, reader.GetString());
        ///     }
        /// </code></example>
        /// <seealso cref="RequireObject(ref Utf8JsonReader)"/>
        /// <seealso cref="RequireObjectOrNull(ref Utf8JsonReader)"/>
        public ref struct ObjectHelper
        {
            private readonly bool _defined;
            private readonly string[] _requiredPropertyNames;
            private UInt64 _requiredPropertyBits;
            private string _name;
            private SequencePosition? _valueStartPos;

            /// <summary>
            /// The name of the last property that was read, or null if none.
            /// </summary>
            public string Name => _name;

            internal ObjectHelper(bool defined, string[] requiredProperties)
            {
                _defined = defined;
                _requiredPropertyNames = requiredProperties;
                if (requiredProperties != null && requiredProperties.Length != 0)
                {
                    if (requiredProperties.Length > 63)
                    {
                        throw new ArgumentException("can't specify more than 63 required properties");
                    }
                    _requiredPropertyBits = (((UInt64)1 << requiredProperties.Length) - 1);
                }
                else
                {
                    _requiredPropertyBits = 0;
                }
                _name = null;
                _valueStartPos = null;
            }

            /// <summary>
            /// Adds a requirement that the specified JSON property name(s) must appear in the JSON object at
            /// some point before it ends.
            /// </summary>
            /// <remarks>
            /// <para>
            /// This method returns a new, modified <see cref="ObjectHelper"/>. It should be called before
            /// the first time you call <see cref="Next"/>. For instance:
            /// </para>
            /// <code>
            ///     var requiredProps = new string[] { "key", "name" };
            ///     for (var obj = RequireObject(ref reader).WithRequiredProperties(requiredProps);
            ///         obj.Next(ref reader);)
            ///     {
            ///         switch (obj.Name) { ... }
            ///     }
            /// </code>
            /// <para>
            /// When the end of the object is reached, if one of the required properties has not yet been
            /// seen, <see cref="Next"/> will throw a <see cref="JsonException"/>.
            /// </para>
            /// <para>
            /// For efficiency, it is best to preallocate the list of property names globally rather than
            /// creating it inline.
            /// </para>
            /// <para>
            /// The current implementation does not allow more than 63 required properties.
            /// </para>
            /// </remarks>
            /// <param name="propertyNames">the required property names</param>
            /// <returns>an updated <see cref="ObjectReader"/></returns>
            public ObjectHelper WithRequiredProperties(params string[] propertyNames) =>
                new ObjectHelper(_defined, propertyNames);

            /// <summary>
            /// Tries to read the next object property. If successful, it sets <see cref="Name"/>, and
            /// then calls <c>Read</c> again so that the parser is now at the property value. If the consumer
            /// did not consume the previous property value, it calls <c>Skip</c> first (so you do not need
            /// to remember to skip values of unrecognized properties when iterating over an object).
            /// </summary>
            /// <param name="reader">the JSON reader (this must be passed again due to ref struct rules)</param>
            /// <returns>true if there is a property, false if this is the end</returns>
            public bool Next(scoped ref Utf8JsonReader reader)
            {
                if (!_defined)
                {
                    return false;
                }
                if (_valueStartPos.HasValue)
                {
                    if (reader.Position.Equals(_valueStartPos.Value))
                    {
                        reader.Skip();
                    }
                    _valueStartPos = null;
                }

                if (!reader.Read() || reader.TokenType == JsonTokenType.EndObject)
                {
                    _name = null;

                    if (_requiredPropertyBits != 0)
                    {
                        for (int i = 0; i < _requiredPropertyNames.Length; i++)
                        {
                            if ((_requiredPropertyBits & ((UInt64)1 << i)) != 0)
                            {
                                throw new JsonException("Missing required property: " + _requiredPropertyNames[i]);
                            }
                        }
                    }

                    return false;
                }
                _name = reader.GetString();
                reader.Read();
                _valueStartPos = reader.Position;

                if (_requiredPropertyBits != 0)
                {
                    for (int i = 0; i < _requiredPropertyNames.Length; i++)
                    {
                        if (_name.Equals(_requiredPropertyNames[i]))
                        {
                            _requiredPropertyBits &= ~((UInt64)1 << i);
                            break;
                        }
                    }
                }

                return true;
            }
        }
    }
}

