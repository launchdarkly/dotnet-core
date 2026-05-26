#if NET7_0_OR_GREATER
using System.Text.Json.Serialization;

namespace LaunchDarkly.Sdk.Json
{
    [JsonSerializable(typeof(LdValue))]
    [JsonSerializable(typeof(Context))]
    internal partial class LdJsonSerializerContext : JsonSerializerContext
    {
        
    }
}
#endif
