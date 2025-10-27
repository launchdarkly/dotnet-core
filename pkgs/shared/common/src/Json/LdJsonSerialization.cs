using System;
using System.Text.Json;
#if NET7_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
#endif

namespace LaunchDarkly.Sdk.Json
{
    /// <summary>
    /// Helper methods for JSON serialization of SDK classes.
    /// </summary>
    /// <remarks>
    /// These methods can be used with any SDK type that has the <see cref="IJsonSerializable"/>
    /// marker interface.
    /// </remarks>
    public static class LdJsonSerialization
    {
#if NET7_0_OR_GREATER
        internal const string SerializationUnreferencedCodeMessage = "JSON serialization and deserialization might require types that cannot be statically analyzed. Use the overload that takes a JsonTypeInfo or JsonSerializerContext, or make sure all of the required types are preserved.";
        internal const string SerializationRequiresDynamicCodeMessage = "JSON serialization and deserialization might require types that cannot be statically analyzed and might need runtime code generation. Use System.Text.Json source generation for native AOT applications.";
#endif  
        /// <summary>
        /// Converts an object to its JSON representation.
        /// </summary>
        /// <remarks>
        /// This is exactly equivalent to the <c>System.Text.Json</c> method <c>JsonSerializer.Serialize</c>,
        /// except that it only accepts LaunchDarkly types that have the <see cref="IJsonSerializable"/>
        /// marker interface. It is retained for backward compatibility.
        /// </remarks>
        /// <typeparam name="T">type of the object being serialized</typeparam>
        /// <param name="instance">the instance to serialize</param>
        /// <returns>the object's JSON encoding as a string</returns>
#if NET7_0_OR_GREATER
        [RequiresUnreferencedCode(SerializationUnreferencedCodeMessage)]
        [RequiresDynamicCode(SerializationRequiresDynamicCodeMessage)]
#endif
        public static string SerializeObject<T>(T instance) where T : IJsonSerializable =>
            JsonSerializer.Serialize(instance);

        /// <summary>
        /// Converts an object to its JSON representation as a UTF-8 byte array.
        /// </summary>
        /// <remarks>
        /// This is exactly equivalent to the <c>System.Text.Json</c> method <c>JsonSerializer.SerializeToUtf8Bytes</c>,
        /// except that it only accepts LaunchDarkly types that have the <see cref="IJsonSerializable"/>
        /// marker interface. It is retained for backward compatibility.
        /// </remarks>
        /// <typeparam name="T">type of the object being serialized</typeparam>
        /// <param name="instance">the instance to serialize</param>
        /// <returns>the object's JSON encoding as a byte array</returns>
#if NET7_0_OR_GREATER
        [RequiresUnreferencedCode(SerializationUnreferencedCodeMessage)]
        [RequiresDynamicCode(SerializationRequiresDynamicCodeMessage)]
#endif
        public static byte[] SerializeObjectToUtf8Bytes<T>(T instance) where T : IJsonSerializable =>
            JsonSerializer.SerializeToUtf8Bytes(instance);

        /// <summary>
        /// Parses an object from its JSON representation.
        /// </summary>
        /// <remarks>
        /// This is exactly equivalent to the <c>System.Text.Json</c> method <c>JsonSerializer.Deserialize</c>,
        /// except that it only accepts LaunchDarkly types that have the <see cref="IJsonSerializable"/>
        /// marker interface. It is retained for backward compatibility.
        /// </remarks>
        /// <typeparam name="T">type of the object being deserialized</typeparam>
        /// <param name="json">the object's JSON encoding as a string</param>
        /// <returns>the deserialized instance</returns>
        /// <exception cref="JsonException">if the JSON encoding was invalid</exception>
#if NET7_0_OR_GREATER
        [RequiresUnreferencedCode(SerializationUnreferencedCodeMessage)]
        [RequiresDynamicCode(SerializationRequiresDynamicCodeMessage)]
#endif
        public static T DeserializeObject<T>(string json) where T : IJsonSerializable =>
            JsonSerializer.Deserialize<T>(json);
    }
}
