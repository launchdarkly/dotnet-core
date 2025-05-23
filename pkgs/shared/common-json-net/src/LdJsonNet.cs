using System;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LaunchDarkly.Sdk.Json
{
    /// <summary>
    /// Integration between the LaunchDarkly SDKs and Json.NET (<c>Newtonsoft.Json</c>).
    /// </summary>
    /// <remarks>
    /// Earlier versions of the LaunchDarkly SDKs used Json.NET internally, so SDK types like
    /// <c>LdValue</c> and <c>User</c> that define their own custom serializations would automatically
    /// use that custom logic when being serialized or deserialized with Json.NET. However, the SDKs
    /// now use a different framework internally, and Json.NET will no longer handle those types
    /// correctly without a separate configuration step; see <see cref="Converter"/>.
    /// </remarks>
    public static class LdJsonNet
    {
        // The IJsonSerializable interface, which is what tells us whether this is one of the SDK's
        // serializable types, is defined in the main LaunchDarkly.CommonSdk package. However, we
        // don't actually need any code in that package in order to make this adapter work; all we
        // really need is System.Text.Json. So it's preferable to avoid having a dependency on
        // LaunchDarkly.CommonSdk from LaunchDarkly.CommonSdk.JsonNet (it would make our release
        // process less convenient and potentially cause version conflicts); instead, we can just
        // look up the interface type dynamically like this.
        internal static readonly Type IJsonSerializableType = DetectSerializableInterfaceType();

        /// <summary>
        /// A <c>JsonConverter</c> that allows Json.NET to serialize and deserialize LaunchDarkly
        /// SDK types.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This converter tells <c>Newtonsoft.Json.JsonConvert</c> how to use the appropriate logic
        /// LaunchDarkly SDK types like <c>User</c> (more generally, any SDK type that has the marker
        /// interface <c>LaunchDarkly.Sdk.Json.IJsonSerializable</c>). There are several ways to use it:
        /// </para>
        /// <para>
        /// 1. Pass it as a parameter to an individual `SerializeObject` or `DeserializeObject` call,
        /// if the top-level type you are serializing is one of the SDK types.
        /// </para>
        /// <example>
        /// <code>
        ///     var user = LaunchDarkly.Sdk.User.WithKey("user-key");
        ///     var userJson = JsonConvert.SerializeObject(user,
        ///         LaunchDarkly.Sdk.Json.LdJsonNet.Converter);
        /// </code>
        /// </example>
        /// <para>
        /// 2. Add it to the list of converters in a <c>JsonSerializerSettings</c> instance and pass
        /// those settings. This works even if the SDK types are contained in some other type.
        /// </para>
        /// <example>
        /// <code>
        ///     var settings = new JsonSerializerSettings
        ///     {
        ///         Converters = new List&lt;JsonConverter&gt;
        ///         {
        ///             LaunchDarkly.Sdk.Json.LdJsonNet.Converter
        ///             // and any other custom converters you may have
        ///         }
        ///     };
        ///     var myObject = new MyClass { User = user };
        ///     var json = JsonConvert.SerializeObject(myObject, settings);
        /// </code>
        /// <para>
        /// 3. Same as 2, but modifying the global default settings.
        /// </para>
        /// <code>
        ///     JsonConvert.DefaultSettings = () => new JsonSerializerSettings
        ///     {
        ///         Converters = new List&lt;JsonConverter&gt;
        ///         {
        ///             LaunchDarkly.Sdk.Json.LdJsonNet.Converter
        ///             // and any other custom converters you may have
        ///         }
        ///     };
        ///     var myObject = new MyClass { User = user };
        ///     var json = JsonConvert.SerializeObject(myObject);
        /// </code>
        /// </example>
        /// </remarks>
        public static JsonConverter Converter =>
            IJsonSerializableType is null ?
                throw new InvalidOperationException("LdJsonNet.Converter cannot be used unless a LaunchDarkly .NET SDK is present") :
                JsonConverterFactory.Instance;

        private static Type DetectSerializableInterfaceType()
        {
            try
            {
                var sdkCommonAssembly = Assembly.Load("LaunchDarkly.CommonSdk");
                var t = sdkCommonAssembly.GetType("LaunchDarkly.Sdk.Json.IJsonSerializable");
                return t;
            }
            catch { }
            return null;
        }
    }

    internal class JsonConverterFactory : JsonConverter
    {
        internal static readonly JsonConverter Instance = new JsonConverterFactory();

        // Json.NET has idiosyncratic default behavior, e.g. it will try to convert strings to DateTime
        // instances if it thinks they look like dates. That's not what we want, so we'll configure our
        // own Serializer instance here.
        internal static readonly JsonSerializer DefaultSerializer = new JsonSerializer()
        {
            DateParseHandling = DateParseHandling.None
        };

        public override bool CanConvert(Type objectType) =>
            LdJsonNet.IJsonSerializableType.IsAssignableFrom(objectType) ||
            objectType.IsGenericType && objectType.GetGenericTypeDefinition() == typeof(Nullable<>) &&
                LdJsonNet.IJsonSerializableType.IsAssignableFrom(Nullable.GetUnderlyingType(objectType));

        public override bool CanRead => true;

        public override bool CanWrite => true;

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (objectType.IsGenericType && objectType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                objectType = Nullable.GetUnderlyingType(objectType);
                if (reader.TokenType == JsonToken.Null)
                {
                    return null;
                }
            }
            var raw = DefaultSerializer.Deserialize<JRaw>(reader);
            return System.Text.Json.JsonSerializer.Deserialize(raw.Value.ToString(), objectType);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            string json = System.Text.Json.JsonSerializer.Serialize(value);
            writer.WriteRawValue(json);
        }
    }
}
