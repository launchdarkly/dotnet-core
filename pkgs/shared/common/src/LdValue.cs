using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using LaunchDarkly.Sdk.Internal.Helpers;
using LaunchDarkly.Sdk.Json;

namespace LaunchDarkly.Sdk
{
    // Note, internal classes used here are in LdValueHelpers.cs

    /// <summary>
    /// Describes the type of a JSON value.
    /// </summary>
    public enum LdValueType
    {
        /// <summary>
        /// The value is null.
        /// </summary>
        Null,
        /// <summary>
        /// The value is a boolean.
        /// </summary>
        Bool,
        /// <summary>
        /// The value is numeric. JSON does not have separate types for int and float,
        /// but you can convert to either.
        /// </summary>
        Number,
        /// <summary>
        /// The value is a string.
        /// </summary>
        String,
        /// <summary>
        /// The value is an array.
        /// </summary>
        Array,
        /// <summary>
        /// The value is an object (a.k.a. hash or dictionary).
        /// </summary>
        Object
    }

    /// <summary>
    /// An immutable instance of any data type that is allowed in JSON.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is used as the return type of the client's JsonVariation method, and also as
    /// the type of custom attributes in <see cref="User"/> and <see cref="IUserBuilder"/>.
    /// </para>
    /// <para>
    /// LaunchDarkly allows feature flag variations and custom user attributes to be of any JSON
    /// type, with some restrictions (notably, regarding numeric precision). For more details, see
    /// our documentation on <see href="https://docs.launchdarkly.com/sdk/concepts/flag-types">flag
    /// value types</see>.
    /// </para>
    /// <para>
    /// Note that this is a <see langword="struct"/>, not a class, so it is always passed by value
    /// and is not nullable; JSON nulls are represented by the constant <see cref="Null"/> and can
    /// be detected with <see cref="IsNull"/>. Whenever possible, <see cref="LdValue"/>
    /// stores primitive types within the struct rather than allocating an object on the heap.
    /// </para>
    /// <para>
    /// There are several ways to create an <see cref="LdValue"/>. For primitive types,
    /// use the various overloads of "Of" such as <see cref="Of(bool)"/>; these are very efficient
    /// since they do not allocate any objects on the heap. For arrays and objects (dictionaries),
    /// use <see cref="ArrayFrom(IEnumerable{LdValue})"/>, <see cref="ArrayOf(LdValue[])"/>,
    /// <see cref="ObjectFrom(IReadOnlyDictionary{string, LdValue})"/>, or the corresponding
    /// methods in the type-specific <see cref="Convert"/> instances.
    /// </para>
    /// <para>
    /// To convert to other types, there are the "As" properties such as 
    /// use the various overloads of "Of" such as <see cref="Of(bool)"/>; these are very efficient
    /// since they do not allocate any objects on the heap. For arrays and objects (dictionaries),
    /// use <see cref="AsList{T}(LdValue.Converter{T})"/> or <see cref="AsDictionary{T}(LdValue.Converter{T})"/>.
    /// </para>
    /// </remarks>
    [JsonConverter(typeof(LdJsonConverters.LdValueConverter))]
    public struct LdValue : IEquatable<LdValue>, IJsonSerializable
    {
        #region Private fields

        private static readonly LdValue _nullInstance = new LdValue(LdValueType.Null, false, 0, null);

        private readonly LdValueType _type;
        private readonly bool _boolValue;
        private readonly double _doubleValue; // all numbers are stored as double
        private readonly string _stringValue;
        private readonly ImmutableList<LdValue> _arrayValue;
        private readonly ImmutableDictionary<string, LdValue> _objectValue;

        #endregion

        #region Public static properties

        /// <summary>
        /// Convenience property for an <see cref="LdValue"/> that wraps a <see langword="null"/> value.
        /// </summary>
        public static LdValue Null => _nullInstance;

        #endregion

        #region Internal/private constructors, factory, and properties

        // Constructor from a primitive type
        private LdValue(LdValueType type, bool boolValue, double doubleValue, string stringValue)
        {
            _type = type;
            _boolValue = boolValue;
            _doubleValue = doubleValue;
            _stringValue = stringValue;
            _arrayValue = null;
            _objectValue = null;
        }

        // Constructor from a read-only list
        private LdValue(ImmutableList<LdValue> list)
        {
            _type = LdValueType.Array;
            _arrayValue = list;
            _boolValue = false;
            _doubleValue = 0;
            _stringValue = null;
            _objectValue = null;
        }

        // Constructor from a read-only dictionary
        private LdValue(ImmutableDictionary<string, LdValue> dict)
        {
            _type = LdValueType.Object;
            _objectValue = dict;
            _boolValue = false;
            _doubleValue = 0;
            _stringValue = null;
            _arrayValue = null;
        }

        #endregion

        #region Public factory methods

        /// <summary>
        /// Initializes an <see cref="LdValue"/> from a boolean value.
        /// </summary>
        /// <param name="value">the initial value</param>
        /// <returns>a struct that wraps the value</returns>
        public static LdValue Of(bool value) =>
            new LdValue(LdValueType.Bool, value, 0, null);

        /// <summary>
        /// Initializes an <see cref="LdValue"/> from an <see langword="int"/> value.
        /// </summary>
        /// <param name="value">the initial value</param>
        /// <returns>a struct that wraps the value</returns>
        public static LdValue Of(int value) =>
            new LdValue(LdValueType.Number, false, value, null);

        /// <summary>
        /// Initializes an <see cref="LdValue"/> from a <see langword="long"/> value.
        /// </summary>
        /// <remarks>
        /// Numeric values in LaunchDarkly have some precision limitations. For more details, see our
        /// documentation on <see href="https://docs.launchdarkly.com/sdk/concepts/flag-types">flag
        /// value types</see>.
        /// </remarks>
        /// <param name="value">the initial value</param>
        /// <returns>a struct that wraps the value</returns>
        public static LdValue Of(long value) =>
            new LdValue(LdValueType.Number, false, value, null);

        /// <summary>
        /// Initializes an <see cref="LdValue"/> from a <see langword="float"/> value.
        /// </summary>
        /// <remarks>
        /// Numeric values in LaunchDarkly have some precision limitations. For more details, see our
        /// documentation on <see href="https://docs.launchdarkly.com/sdk/concepts/flag-types">flag
        /// value types</see>.
        /// </remarks>
        /// <param name="value">the initial value</param>
        /// <returns>a struct that wraps the value</returns>
        public static LdValue Of(float value) =>
            new LdValue(LdValueType.Number, false, value, null);

        /// <summary>
        /// Initializes an <see cref="LdValue"/> from a <see langword="double"/> value.
        /// </summary>
        /// <remarks>
        /// Numeric values in LaunchDarkly have some precision limitations. For more details, see our
        /// documentation on <see href="https://docs.launchdarkly.com/sdk/concepts/flag-types">flag
        /// value types</see>.
        /// </remarks>
        /// <param name="value">the initial value</param>
        /// <returns>a struct that wraps the value</returns>
        public static LdValue Of(double value) =>
            new LdValue(LdValueType.Number, false, value, null);

        /// <summary>
        /// Initializes an <see cref="LdValue"/> from a string value.
        /// </summary>
        /// <remarks>
        /// A null string reference will be stored as <see cref="Null"/> rather than as a string.
        /// </remarks>
        /// <param name="value">the initial value</param>
        /// <returns>a struct that wraps the value</returns>
        public static LdValue Of(string value) =>
            value is null ? Null : new LdValue(LdValueType.String, false, 0, value);

        /// <summary>
        /// Initializes an <see cref="LdValue"/> as an array, from a sequence of JSON values.
        /// </summary>
        /// <remarks>
        /// To create an array from values of some other type, use <see cref="Converter{T}.ArrayFrom(IEnumerable{T})"/>
        /// </remarks>
        /// <example>
        /// <code>
        ///     var listOfValues = new List&lt;LdValue&gt; { LdValue.Of(1), LdValue.Of("x") };
        ///     var arrayValue = LdValue.ArrayFrom(listOfValues);
        /// </code>
        /// </example>
        /// <param name="values">a sequence of values</param>
        /// <returns>a struct representing a JSON array, or <see cref="Null"/> if the parameter was null</returns>
        public static LdValue ArrayFrom(IEnumerable<LdValue> values) =>
            Convert.Json.ArrayFrom(values);

        /// <summary>
        /// Initializes an <see cref="LdValue"/> as an array, from a sequence of JSON values.
        /// </summary>
        /// <remarks>
        /// To create an array from values of some other type, use <see cref="Converter{T}.ArrayOf(T[])"/>
        /// </remarks>
        /// <example>
        /// <code>
        ///     var arrayValue = LdValue.ArrayFrom(LdValue.Of("a"), LdValue.Of("b"));
        /// </code>
        /// </example>
        /// <param name="values">any number of values</param>
        /// <returns>a struct representing a JSON array</returns>
        public static LdValue ArrayOf(params LdValue[] values) =>
            Convert.Json.ArrayOf(values);

        /// <summary>
        /// Starts building an array value.
        /// </summary>
        /// <returns>an <see cref="ArrayBuilder"/></returns>
        public static ArrayBuilder BuildArray()
        {
            return new ArrayBuilder();
        }

        /// <summary>
        /// Initializes an <see cref="LdValue"/> as a JSON object, from a dictionary.
        /// </summary>
        /// <remarks>
        /// To use a dictionary with values of some other type, use <see cref="Converter{T}.ObjectFrom"/>.
        /// </remarks>
        /// <param name="dictionary">a dictionary with string keys and values of the specified type</param>
        /// <returns>a struct representing a JSON object, or <see cref="Null"/> if the parameter was null</returns>
        public static LdValue ObjectFrom(IReadOnlyDictionary<string, LdValue> dictionary) =>
            Convert.Json.ObjectFrom(dictionary);

        /// <summary>
        /// Starts building an object value.
        /// </summary>
        /// <returns>an <see cref="ObjectBuilder"/></returns>
        public static ObjectBuilder BuildObject() =>
            new ObjectBuilder();

        /// <summary>
        /// Parses a value from a JSON-encoded string.
        /// </summary>
        /// <example>
        /// <code>
        ///     var myValue = LdValue.Parse("[1,2]");
        ///     Assert.Equal(LdValue.BuildArray().Add(1).Add(2).Build(), myValue); // true
        /// </code>
        /// </example>
        /// <param name="jsonString">a JSON string</param>
        /// <returns>the equivalent <see cref="LdValue"/></returns>
        /// <exception cref="JsonException">if the string could not be parsed as JSON</exception>
        /// <see cref="ToJsonString"/>
        public static LdValue Parse(string jsonString) =>
            LdJsonSerialization.DeserializeObject<LdValue>(jsonString);

        #endregion

        #region Public properties

        /// <summary>
        /// The type of the JSON value.
        /// </summary>
        public LdValueType Type => _type;

        /// <summary>
        /// True if the wrapped value is <see langword="null"/>.
        /// </summary>
        public bool IsNull => Type == LdValueType.Null;

        /// <summary>
        /// True if the wrapped value is numeric.
        /// </summary>
        public bool IsNumber => Type == LdValueType.Number;

        /// <summary>
        /// True if the wrapped value is an integer.
        /// </summary>
        /// <remarks>
        /// JSON does not have separate types for integer and floating-point values; they are both just
        /// numbers. <see cref="IsInt"/> returns true if and only if the actual numeric value has no
        /// fractional component, so <c>LdValue(2).IsInt</c> and <c>LdValue(2.0f).IsInt</c>
        /// are both true.
        /// </remarks>
        public bool IsInt => IsNumber && (AsFloat == (float)AsInt);

        /// <summary>
        /// True if the wrapped value is a string.
        /// </summary>
        public bool IsString => Type == LdValueType.String;

        /// <summary>
        /// Gets the boolean value if this is a boolean.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If the value is <see langword="null"/> or is not a boolean, this returns <see langword="false"/>.
        /// It will never throw an exception.
        /// </para>
        /// <para>
        /// This is equivalent to calling <see cref="Converter{T}.ToType(LdValue)"/> on
        /// <see cref="LdValue.Convert.Bool"/>.
        /// </para>
        /// </remarks>
        public bool AsBool => Type == LdValueType.Bool && _boolValue;

        /// <summary>
        /// Gets the string value if this is a string.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If the value is <see langword="null"/> or is not a string, this returns <see langword="null"/>.
        /// It will never throw an exception. To get a JSON representation of the value as a string, use
        /// <see cref="ToJsonString"/> instead.
        /// </para>
        /// <para>
        /// This is equivalent to calling <see cref="Converter{T}.ToType(LdValue)"/> on
        /// <see cref="LdValue.Convert.String"/>.
        /// </para>
        /// </remarks>
        public string AsString => Type == LdValueType.String ? _stringValue : null;

        /// <summary>
        /// Gets the value as an <see langword="int"/> if it is numeric.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If the value is <see langword="null"/> or is not numeric, this returns zero. It will
        /// never throw an exception.
        /// </para>
        /// <para>
        /// If the value is a number but not an integer, it will be rounded toward zero.
        /// </para>
        /// <para>
        /// This is equivalent to calling <see cref="Converter{T}.ToType(LdValue)"/> on
        /// <see cref="LdValue.Convert.Int"/>.
        /// </para>
        /// </remarks>
        public int AsInt => (int)Math.Round(AsDouble, MidpointRounding.ToEven);

        /// <summary>
        /// Gets the value as an <see langword="long"/> if it is numeric.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If the value is <see langword="null"/> or is not numeric, this returns zero. It will
        /// never throw an exception.
        /// </para>
        /// <para>
        /// If the value is a number but not an integer, it will be rounded toward zero.
        /// </para>
        /// <para>
        /// This is equivalent to calling <see cref="Converter{T}.ToType(LdValue)"/> on
        /// <see cref="LdValue.Convert.Long"/>.
        /// </para>
        /// </remarks>
        public long AsLong => (long)Math.Round(AsDouble, MidpointRounding.ToEven);

        /// <summary>
        /// Gets the value as an <see langword="float"/> if it is numeric.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If the value is <see langword="null"/> or is not numeric, this returns zero. It will never
        /// throw an exception.
        /// </para>
        /// <para>
        /// This is equivalent to calling <see cref="Converter{T}.ToType(LdValue)"/> on
        /// <see cref="LdValue.Convert.Float"/>.
        /// </para>
        /// </remarks>
        public float AsFloat => (float)AsDouble;

        /// <summary>
        /// Gets the value as an <see langword="double"/> if it is numeric.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If the value is <see langword="null"/> or is not numeric, this returns zero. It will never
        /// throw an exception.
        /// </para>
        /// <para>
        /// This is equivalent to calling <see cref="Converter{T}.ToType(LdValue)"/> on
        /// <see cref="LdValue.Convert.Double"/>.
        /// </para>
        /// </remarks>
        public double AsDouble => Type == LdValueType.Number ? _doubleValue : 0;

        /// <summary>
        /// Returns an immutable list of values if this value is an array; otherwise an empty list.
        /// </summary>
        public ImmutableList<LdValue> List => _arrayValue ?? ImmutableList<LdValue>.Empty;

        /// <summary>
        /// Returns an immutable dictionary of values if this value is an object; otherwise an empty dictionary.
        /// </summary>
        public ImmutableDictionary<string, LdValue> Dictionary => _objectValue ??
            ImmutableDictionary<string, LdValue>.Empty;

        /// <summary>
        /// The number of values if this is an array or object; otherwise zero.
        /// </summary>
        public int Count
        {
            get
            {
                switch (_type)
                {
                    case LdValueType.Array:
                        return _arrayValue.Count;
                    case LdValueType.Object:
                        return _objectValue.Count;
                    default:
                        return 0;
                }
            }
        }
        
        #endregion

        #region Public methods

        /// <summary>
        /// Retrieves an array item or object key by index. Never throws an exception.
        /// </summary>
        /// <param name="index">the item index</param>
        /// <returns>the item value if this is an array; the key if this is an object; otherwise <see cref="Null"/></returns>
        public LdValue Get(int index)
        {
            switch (_type)
            {
                case LdValueType.Array:
                    return index >= 0 && index < _arrayValue.Count ? _arrayValue[index] : LdValue.Null;
                case LdValueType.Object:
                    return index >= 0 && index < _objectValue.Count ? LdValue.Of(_objectValue.Keys.ElementAt(index)) : LdValue.Null;
                default:
                    return LdValue.Null;
            }
        }

        /// <summary>
        /// Retrieves a object value by key. Never throws an exception.
        /// </summary>
        /// <param name="key">the key to retrieve</param>
        /// <returns>the value for the key, if this is an object; <see cref="Null"/> if not found, or if this is not an object</returns>
        public LdValue Get(string key)
        {
            return _type == LdValueType.Object && _objectValue.TryGetValue(key, out var value)
                ? value
                : LdValue.Null;
        }

        /// <summary>
        /// Converts the value to a read-only list of elements of some type.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The first parameter is one of the type converters from <see cref="Convert"/>, or your own
        /// implementation of <see cref="Converter{T}"/> for some type.
        /// </para>
        /// <para>
        /// If the value is not a JSON array at all, an empty list is returned. This method will
        /// never throw an exception.
        /// </para>
        /// <para>
        /// This is an efficient method because it does not copy values to a new list, but returns
        /// a read-only view into the existing array.
        /// </para>
        /// </remarks>
        /// <typeparam name="T">the element type</typeparam>
        /// <returns>an array of elements of the specified type</returns>
        public IReadOnlyList<T> AsList<T>(Converter<T> desiredType)
        {
            if (_type == LdValueType.Array)
            {
                return new LdValueListConverter<LdValue, T>(_arrayValue, desiredType.ToType);
            }
            return new LdValueListConverter<T, T>(null, null);
        }

        /// <summary>
        /// Converts the value to a read-only dictionary.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The first parameter is one of the type converters from <see cref="Convert"/>, or your own
        /// implementation of <see cref="Converter{T}"/> for some type.
        /// </para>
        /// <para>
        /// This is an efficient method because it does not copy values to a new dictionary, but returns
        /// a read-only view into the existing object.
        /// </para>
        /// </remarks>
        /// <returns>a read-only dictionary</returns>
        public IReadOnlyDictionary<string, T> AsDictionary<T>(Converter<T> desiredType)
        {
            if (_type == LdValueType.Object)
            {
                return new LdValueDictionaryConverter<LdValue, T>(_objectValue, desiredType.ToType);
            }
            return new LdValueDictionaryConverter<T, T>(null, null);
        }
        
        /// <summary>
        /// Converts the value to its JSON encoding.
        /// </summary>
        /// <remarks>
        /// For instance, <c>LdValue.Of(1).ToJsonString()</c> returns <c>"1"</c>;
        /// <c>LdValue.Of("x").ToJsonString()</c> returns <c>"\"x\""</c>; and
        /// <c>LdValue.Null.ToJsonString()</c> returns <c>"null"</c>.
        /// </remarks>
        /// <returns>the JSON encoding of the value</returns>
        /// <see cref="Parse(string)"/>
        public string ToJsonString()
        {
            switch (_type)
            {
                case LdValueType.Null:
                    return "null";
                case LdValueType.Bool:
                    return _boolValue ? "true" : "false";
                default:
                    return JsonSerializer.Serialize(this);
            }
        }

        /// <summary>
        /// Performs a deep-equality comparison.
        /// </summary>
        public override bool Equals(object o) => (o is LdValue v) && Equals(v);

        /// <summary>
        /// Performs a deep-equality comparison.
        /// </summary>
        public bool Equals(LdValue o)
        {
            if (Type != o.Type)
            {
                return false;
            }
            switch (Type)
            {
                case LdValueType.Null:
                    return true;
                case LdValueType.Bool:
                    return AsBool == o.AsBool;
                case LdValueType.Number:
                    return AsDouble == o.AsDouble; // don't worry about ints because you can't lose precision going from int to double
                case LdValueType.String:
                    return AsString.Equals(o.AsString);
                case LdValueType.Array:
                    return AsList(Convert.Json).SequenceEqual(o.AsList(Convert.Json));
                case LdValueType.Object:
                    {
                        var d0 = AsDictionary(Convert.Json);
                        var d1 = o.AsDictionary(Convert.Json);
                        return d0.Count == d1.Count && d0.All(kv =>
                            d1.TryGetValue(kv.Key, out var v) && kv.Value.Equals(v));
                    }
                default:
                    return false;
            }
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            switch (Type)
            {
                case LdValueType.Null:
                    return 0;
                case LdValueType.Bool:
                    return AsBool.GetHashCode();
                case LdValueType.Number:
                    return AsFloat.GetHashCode();
                case LdValueType.String:
                    return AsString.GetHashCode();
                case LdValueType.Array:
                    {
                        var h = new HashCodeBuilder();
                        foreach (var item in AsList(Convert.Json))
                        {
                            h = h.With(item);
                        }
                        return h.Value;
                    }
                case LdValueType.Object:
                    {
                        var h = new HashCodeBuilder();
                        var d = AsDictionary(Convert.Json);
                        var keys = d.Keys.ToArray();
                        Array.Sort(keys); // inefficient, but ensures determinacy
                        foreach (var key in keys)
                        {
                            h = h.With(key).With(d[key]);
                        }
                        return h.Value;
                    }
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Converts the value to its JSON encoding (same as <see cref="ToJsonString"/>).
        /// </summary>
        /// <returns>the JSON encoding of the value</returns>
        public override string ToString() => ToJsonString();

#pragma warning disable CS1591  // don't need XML comments for these standard methods
        public static bool operator ==(LdValue a, LdValue b) => a.Equals(b);

        public static bool operator !=(LdValue a, LdValue b) => !a.Equals(b);
#pragma warning restore CS1591

        #endregion

        #region Inner types

        /// <summary>
        /// An object returned by <see cref="LdValue.BuildArray"/> for building an array of values.
        /// </summary>
        public sealed class ArrayBuilder
        {
            private ImmutableList<LdValue>.Builder _builder = ImmutableList.CreateBuilder<LdValue>();

            internal ArrayBuilder() { }

            /// <summary>
            /// Adds a value to the array being built.
            /// </summary>
            /// <param name="value">the value to add</param>
            /// <returns>the same builder</returns>
            public ArrayBuilder Add(LdValue value)
            {
                _builder.Add(value);
                return this;
            }

            /// <summary>
            /// Adds a value to the array being built.
            /// </summary>
            /// <param name="value">the value to add</param>
            /// <returns>the same builder</returns>
            public ArrayBuilder Add(bool value)
            {
                _builder.Add(LdValue.Of(value));
                return this;
            }

            /// <summary>
            /// Adds a value to the array being built.
            /// </summary>
            /// <remarks>
            /// Numeric values in LaunchDarkly have some precision limitations. For more details, see our
            /// documentation on <see href="https://docs.launchdarkly.com/sdk/concepts/flag-types">flag
            /// value types</see>.
            /// </remarks>
            /// <param name="value">the value to add</param>
            /// <returns>the same builder</returns>
            public ArrayBuilder Add(long value)
            {
                _builder.Add(LdValue.Of(value));
                return this;
            }

            /// <summary>
            /// Adds a value to the array being built.
            /// </summary>
            /// <remarks>
            /// Numeric values in LaunchDarkly have some precision limitations. For more details, see our
            /// documentation on <see href="https://docs.launchdarkly.com/sdk/concepts/flag-types">flag
            /// value types</see>.
            /// </remarks>
            /// <param name="value">the value to add</param>
            /// <returns>the same builder</returns>
            public ArrayBuilder Add(double value)
            {
                _builder.Add(LdValue.Of(value));
                return this;
            }

            /// <summary>
            /// Adds a value to the array being built.
            /// </summary>
            /// <param name="value">the value to add</param>
            /// <returns>the same builder</returns>
            public ArrayBuilder Add(string value)
            {
                _builder.Add(LdValue.Of(value));
                return this;
            }

            /// <summary>
            /// Returns an array value containing the items provided so far.
            /// </summary>
            /// <returns>an immutable array <see cref="LdValue"/></returns>
            public LdValue Build()
            {
                return new LdValue(_builder.ToImmutable());
            }
        }

        /// <summary>
        /// An object returned by <see cref="LdValue.BuildObject"/> for building an object from keys and values.
        /// </summary>
        public sealed class ObjectBuilder
        {
            private ImmutableDictionary<string, LdValue>.Builder _builder = ImmutableDictionary.CreateBuilder<string, LdValue>();

            internal ObjectBuilder() { }

            /// <summary>
            /// Adds a key-value pair to the object being built.
            /// </summary>
            /// <param name="key">the key to add</param>
            /// <param name="value">the value to add</param>
            /// <returns>the same builder</returns>
            public ObjectBuilder Add(string key, LdValue value)
            {
                _builder.Add(key, value);
                return this;
            }

            /// <summary>
            /// Adds a key-value pair to the object being built.
            /// </summary>
            /// <param name="key">the key to add</param>
            /// <param name="value">the value to add</param>
            /// <returns>the same builder</returns>
            public ObjectBuilder Add(string key, bool value) =>
                Add(key, LdValue.Of(value));

            /// <summary>
            /// Adds a key-value pair to the object being built.
            /// </summary>
            /// <remarks>
            /// Numeric values in LaunchDarkly have some precision limitations. For more details, see our
            /// documentation on <see href="https://docs.launchdarkly.com/sdk/concepts/flag-types">flag
            /// value types</see>.
            /// </remarks>
            /// <param name="key">the key to add</param>
            /// <param name="value">the value to add</param>
            /// <returns>the same builder</returns>
            public ObjectBuilder Add(string key, long value) =>
                Add(key, LdValue.Of(value));

            /// <summary>
            /// Adds a key-value pair to the object being built.
            /// </summary>
            /// <remarks>
            /// Numeric values in LaunchDarkly have some precision limitations. For more details, see our
            /// documentation on <see href="https://docs.launchdarkly.com/sdk/concepts/flag-types">flag
            /// value types</see>.
            /// </remarks>
            /// <param name="key">the key to add</param>
            /// <param name="value">the value to add</param>
            /// <returns>the same builder</returns>
            public ObjectBuilder Add(string key, double value) =>
                Add(key, LdValue.Of(value));

            /// <summary>
            /// Removes a key from the object, or does nothing if no such key exists.
            /// </summary>
            /// <param name="key">the key</param>
            /// <returns>the same builder</returns>
            public ObjectBuilder Remove(string key)
            {
                _builder.Remove(key);
                return this;
            }

            /// <summary>
            /// Adds a key-value pair to the object being built or replaces an existing key.
            /// </summary>
            /// <param name="key">the key</param>
            /// <param name="value">the value to add or replace</param>
            /// <returns>the same builder</returns>
            public ObjectBuilder Set(string key, LdValue value) =>
                Remove(key).Add(key, value);

            /// <summary>
            /// Adds a key-value pair to the object being built or replaces an existing key.
            /// </summary>
            /// <param name="key">the key</param>
            /// <param name="value">the value to add or replace</param>
            /// <returns>the same builder</returns>
            public ObjectBuilder Set(string key, bool value) =>
                Set(key, LdValue.Of(value));

            /// <summary>
            /// Adds a key-value pair to the object being built or replaces an existing key.
            /// </summary>
            /// <remarks>
            /// Numeric values in LaunchDarkly have some precision limitations. For more details, see our
            /// documentation on <see href="https://docs.launchdarkly.com/sdk/concepts/flag-types">flag
            /// value types</see>.
            /// </remarks>
            /// <param name="key">the key</param>
            /// <param name="value">the value to add or replace</param>
            /// <returns>the same builder</returns>
            public ObjectBuilder Set(string key, long value) =>
                Set(key, LdValue.Of(value));

            /// <summary>
            /// Adds a key-value pair to the object being built or replaces an existing key.
            /// </summary>
            /// <remarks>
            /// Numeric values in LaunchDarkly have some precision limitations. For more details, see our
            /// documentation on <see href="https://docs.launchdarkly.com/sdk/concepts/flag-types">flag
            /// value types</see>.
            /// </remarks>
            /// <param name="key">the key</param>
            /// <param name="value">the value to add or replace</param>
            /// <returns>the same builder</returns>
            public ObjectBuilder Set(string key, double value) =>
                Set(key, LdValue.Of(value));

            /// <summary>
            /// Adds a key-value pair to the object being built or replaces an existing key.
            /// </summary>
            /// <param name="key">the key</param>
            /// <param name="value">the value to add or replace</param>
            /// <returns>the same builder</returns>
            public ObjectBuilder Set(string key, string value) =>
                Set(key, LdValue.Of(value));

            /// <summary>
            /// Copies existing property keys and values from an existing JSON object; does
            /// nothing if the value is not an object.
            /// </summary>
            /// <param name="fromObject">a JSON value</param>
            /// <returns>the same builder</returns>
            public ObjectBuilder Copy(LdValue fromObject)
            {
                foreach (var kv in fromObject.AsDictionary(Convert.Json))
                {
                    Set(kv.Key, kv.Value);
                }
                return this;
            }

            /// <summary>
            /// Adds a key-value pair to the object being built.
            /// </summary>
            /// <param name="key">the key to add</param>
            /// <param name="value">the value to add</param>
            /// <returns>the same builder</returns>
            public ObjectBuilder Add(string key, string value)
            {
                _builder.Add(key, LdValue.Of(value));
                return this;
            }

            /// <summary>
            /// Returns an object value containing the keys and values provided so far.
            /// </summary>
            /// <returns>an immutable object <see cref="LdValue"/></returns>
            public LdValue Build()
            {
                return new LdValue(_builder.ToImmutable());
            }
        }

        /// <summary>
        /// Defines a conversion between <see cref="LdValue"/> and some other type.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Besides converting individual values, <see cref="Converter{T}"/> provides factory methods
        /// like <see cref="ArrayOf"/> which transform a collection of the specified type to the
        /// corresponding <see cref="LdValue"/> complex type.
        /// </para>
        /// <para>
        /// There are type-specific instances of this class for commonly used types in
        /// <see cref="LdValue.Convert"/>, but you can also implement your own.
        /// </para>
        /// </remarks>
        /// <typeparam name="T">the type to convert from/to</typeparam>
        public abstract class Converter<T>
        {
            /// <summary>
            /// Converts a value of the specified type to an <see cref="LdValue"/>.
            /// </summary>
            /// <remarks>
            /// This method should never throw an exception; if for some reason the value is invalid,
            /// it should return <see cref="LdValue.Null"/>.
            /// </remarks>
            /// <param name="valueOfType">a value of this type</param>
            /// <returns>an <see cref="LdValue"/></returns>
            abstract public LdValue FromType(T valueOfType);

            /// <summary>
            /// Converts an <see cref="LdValue"/> to a value of the specified type.
            /// </summary>
            /// <remarks>
            /// This method should never throw an exception; if the conversion cannot be done, it
            /// should return <c>default(T)</c>.
            /// </remarks>
            /// <param name="jsonValue">an <see cref="LdValue"/></param>
            /// <returns>a value of this type</returns>
            abstract public T ToType(LdValue jsonValue);

            /// <summary>
            /// Initializes an <see cref="LdValue"/> as an array, from a sequence of this type.
            /// </summary>
            /// <remarks>
            /// Values are copied, so subsequent changes to the source values do not affect the array.
            /// </remarks>
            /// <example>
            /// <code>
            ///     var listOfInts = new List&lt;int&gt; { 1, 2, 3 };
            ///     var arrayValue = LdValue.Convert.Int.ArrayFrom(arrayOfInts);
            /// </code>
            /// </example>
            /// <param name="values">a sequence of elements of the specified type</param>
            /// <returns>a struct representing a JSON array, or <see cref="LdValue.Null"/> if the
            /// parameter was null</returns>
            public LdValue ArrayFrom(IEnumerable<T> values)
            {
                if (values is null)
                {
                    return Null;
                }
                return new LdValue(ImmutableList.CreateRange<LdValue>(values.Select(FromType)));
            }

            /// <summary>
            /// Initializes an <see cref="LdValue"/> as an array, from a sequence of this type.
            /// </summary>
            /// <remarks>
            /// Values are copied, so subsequent changes to the source values do not affect the array.
            /// </remarks>
            /// <example>
            /// <code>
            ///     var arrayValue = LdValue.Convert.Int.ArrayOf(1, 2, 3);
            /// </code>
            /// </example>
            /// <param name="values">any number of elements of the specified type</param>
            /// <returns>a struct representing a JSON array</returns>
            public LdValue ArrayOf(params T[] values)
            {
                return ArrayFrom(values);
            }

            /// <summary>
            /// Initializes an <see cref="LdValue"/> as a JSON object, from a dictionary containing
            /// values of this type.
            /// </summary>
            /// <remarks>
            /// Values are copied, so subsequent changes to the source values do not affect the array.
            /// </remarks>
            /// <example>
            /// <code>
            ///     var dictionaryOfInts = new Dictionary&lt;string, int&gt; { { "a", 1 }, { "b", 2 } };
            ///     var objectValue = LdValue.Convert.Int.ObjectFrom(dictionaryOfInts);
            /// </code>
            /// </example>
            /// <param name="dictionary">a dictionary with string keys and values of the specified type</param>
            /// <returns>a struct representing a JSON object, or <see cref="LdValue.Null"/> if the
            /// parameter was null</returns>
            public LdValue ObjectFrom(IReadOnlyDictionary<string, T> dictionary)
            {
                if (dictionary is null)
                {
                    return Null;
                }
                var d = ImmutableDictionary.CreateRange<string, LdValue>(dictionary.Select(kv =>
                    new KeyValuePair<string, LdValue>(kv.Key, FromType(kv.Value))));
                return new LdValue(d);
            }
        }

        /// <summary>
        /// Predefined instances of <see cref="Converter{T}"/> for commonly used types.
        /// </summary>
        /// <remarks>
        /// These are mostly useful for methods that convert <see cref="LdValue"/> to or from a
        /// collection of some type, such as <see cref="Converter{T}.ArrayOf(T[])"/> and
        /// <see cref="LdValue.AsList{T}(Converter{T})"/>.
        /// </remarks>
        public static class Convert
        {
            /// <summary>
            /// A <see cref="Converter{T}"/> for the <see langword="bool"/> type.
            /// </summary>
            /// <remarks>
            /// Its behavior is consistent with <see cref="LdValue.Of(bool)"/> and
            /// <see cref="LdValue.AsBool"/>.
            /// </remarks>
            public static readonly Converter<bool> Bool = new ConverterImpl<bool>(
                v => LdValue.Of(v),
                j => j.AsBool
            );

            /// <summary>
            /// A <see cref="Converter{T}"/> for the <see langword="int"/> type.
            /// </summary>
            /// <remarks>
            /// Its behavior is consistent with <see cref="LdValue.Of(int)"/> and
            /// <see cref="LdValue.AsInt"/>.
            /// </remarks>
            public static readonly Converter<int> Int = new ConverterImpl<int>(
                v => LdValue.Of(v),
                j => j.AsInt
            );

            /// <summary>
            /// A <see cref="Converter{T}"/> for the <see langword="long"/> type.
            /// </summary>
            /// <remarks>
            /// <para>
            /// Its behavior is consistent with <see cref="LdValue.Of(long)"/> and
            /// <see cref="LdValue.AsLong"/>.
            /// </para>
            /// <para>
            /// Note that the LaunchDarkly service, and most of the SDKs, represent numeric values internally
            /// in 64-bit floating-point, which has slightly less precision than a signed 64-bit
            /// <see langword="long"/>; therefore, the full range of <see langword="long"/> values cannot be
            /// accurately represented. If you need to set a user attribute to a numeric value with more
            /// significant digits than will fit in a <see langword="double"/>, it is best to encode it as a string.
            /// </para>
            /// </remarks>
            public static readonly Converter<long> Long = new ConverterImpl<long>(
                v => LdValue.Of(v),
                j => j.AsLong
            );

            /// <summary>
            /// A <see cref="Converter{T}"/> for the <see langword="float"/> type.
            /// </summary>
            /// <remarks>
            /// Its behavior is consistent with <see cref="LdValue.Of(float)"/> and
            /// <see cref="LdValue.AsFloat"/>.
            /// </remarks>
            public static readonly Converter<float> Float = new ConverterImpl<float>(
                v => LdValue.Of(v),
                j => j.AsFloat
            );

            /// <summary>
            /// A <see cref="Converter{T}"/> for the <see langword="double"/> type.
            /// </summary>
            /// <remarks>
            /// Its behavior is consistent with <see cref="LdValue.Of(double)"/> and
            /// <see cref="LdValue.AsDouble"/>.
            /// </remarks>
            public static readonly Converter<double> Double = new ConverterImpl<double>(
                v => LdValue.Of(v),
                j => j.AsDouble
            );

            /// <summary>
            /// A <see cref="Converter{T}"/> for the <see cref="string"/> type.
            /// </summary>
            /// <remarks>
            /// Its behavior is consistent with <see cref="LdValue.Of(string)"/> and
            /// <see cref="LdValue.AsString"/>.
            /// </remarks>
            public static readonly Converter<string> String = new ConverterImpl<string>(
                v => LdValue.Of(v),
                j => j.AsString
            );

            /// <summary>
            /// A <see cref="Converter{T}"/> that indicates the value is an <see cref="LdValue"/>
            /// and does not need to be converted.
            /// </summary>
            public static readonly Converter<LdValue> Json = new ConverterImpl<LdValue>(
                v => v,
                j => j
            );
        }

        private sealed class ConverterImpl<T> : Converter<T>
        {
            private readonly Func<T, LdValue> _fromTypeFn;
            private readonly Func<LdValue, T> _toTypeFn;

            internal ConverterImpl(Func<T, LdValue> fromTypeFn,
                Func<LdValue, T> toTypeFn)
            {
                _fromTypeFn = fromTypeFn;
                _toTypeFn = toTypeFn;
            }

            public override LdValue FromType(T valueOfType) => _fromTypeFn(valueOfType);
            public override T ToType(LdValue jsonValue) => _toTypeFn(jsonValue);
        }

        #endregion
    }
}
