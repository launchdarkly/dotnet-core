using System.Collections.Generic;
using System.Collections.Immutable;

namespace LaunchDarkly.Sdk.Server.Ai.Config;

public static partial class LdAiConfigTypes
{
    /// <summary>
    /// Information about the model.
    /// </summary>
    public sealed record ModelConfig
    {
        /// <summary>
        /// The name of the model.
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// The model's built-in parameters provided by LaunchDarkly.
        /// </summary>
        public readonly IReadOnlyDictionary<string, LdValue> Parameters;

        /// <summary>
        /// The model's custom parameters provided by the user.
        /// </summary>
        public readonly IReadOnlyDictionary<string, LdValue> Custom;

        internal ModelConfig(string name, IReadOnlyDictionary<string, LdValue> parameters, IReadOnlyDictionary<string, LdValue> custom)
        {
            Name = name;
            // Materialize into an ImmutableDictionary so a consumer that downcasts to
            // IDictionary<> can't mutate the stored map. The cast still succeeds (the
            // contract is still IReadOnlyDictionary<>) but write members throw
            // NotSupportedException at runtime, matching the typed read-only promise.
            Parameters = parameters?.ToImmutableDictionary() ?? ImmutableDictionary<string, LdValue>.Empty;
            Custom = custom?.ToImmutableDictionary() ?? ImmutableDictionary<string, LdValue>.Empty;
        }
    }
}
