using System.Collections.Generic;

namespace LaunchDarkly.Sdk.Internal.Events
{
    /// <summary>
    /// Produces a separate summary event for each context, with the context attached, so that
    /// analytics can attribute evaluations to specific contexts. This is used by client-side SDKs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A separate <see cref="EventSummarizer"/> is maintained per context. Contexts are used
    /// directly as dictionary keys; <see cref="Context"/> provides value-based equality and hashing
    /// over all of its attributes, so two contexts map to the same summary only when every attribute
    /// matches.
    /// </para>
    /// <para>
    /// Not thread-safe; always invoked from the event processor's single message-processing thread.
    /// </para>
    /// </remarks>
    internal sealed class PerContextEventSummarizer : IEventSummarizer
    {
        private readonly Dictionary<Context, EventSummarizer> _summarizersByContext =
            new Dictionary<Context, EventSummarizer>();

        public void SummarizeEvent(
            UnixMillisecondTime timestamp,
            string flagKey,
            int? flagVersion,
            int? variation,
            in LdValue value,
            in LdValue defaultValue,
            in Context context
            )
        {
            if (!_summarizersByContext.TryGetValue(context, out var summarizer))
            {
                summarizer = new EventSummarizer(context);
                _summarizersByContext[context] = summarizer;
            }
            summarizer.SummarizeEvent(timestamp, flagKey, flagVersion, variation, value, defaultValue, context);
        }

        public IReadOnlyList<EventSummary> GetSummariesAndReset()
        {
            var summaries = new List<EventSummary>();
            foreach (var summarizer in _summarizersByContext.Values)
            {
                EventSummary summary = summarizer.GetSummaryAndReset();
                if (!summary.Empty)
                {
                    summaries.Add(summary);
                }
            }
            _summarizersByContext.Clear();
            return summaries;
        }

        public void Clear() => _summarizersByContext.Clear();
    }
}
