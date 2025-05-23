
namespace LaunchDarkly.Sdk.Json
{
    /// <summary>
    /// A marker interface for types that define their own JSON serialization rules.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Some types that are defined in the <c>LaunchDarkly.Sdk</c> namespaces, such as
    /// <see cref="User"/> and <see cref="EvaluationReason"/>, have a standard representation
    /// in JSON. The internal structures of these types do not always correspond directly to
    /// the JSON schema, so reflection-based serializers will not work without custom logic.
    /// </para>
    /// </remarks>
    /// <seealso cref="LdJsonSerialization"/>
    public interface IJsonSerializable
    {
    }
}
