using System.Collections.Immutable;
using LaunchDarkly.Sdk.Client.Hooks;

namespace TestService
{
    public class EvaluationHookParams
    {
        public EvaluationSeriesContext EvaluationSeriesContext { get; set; }
        public ImmutableDictionary<string, object> EvaluationSeriesData { get; set; }
        public EvaluateFlagResponse EvaluationDetail { get; set; }
        public string Stage { get; set; }
    }
}
