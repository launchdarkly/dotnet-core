using System;
using System.Collections.Generic;

namespace LaunchDarkly.Sdk.Internal.Events
{
    /// <summary>
    /// Produces a single aggregated summary event covering all contexts, with no context attached.
    /// This is the original summarization behavior and the default; it is used by server-side SDKs.
    /// </summary>
    /// <remarks>
    /// Not thread-safe; always invoked from the event processor's single message-processing thread.
    /// </remarks>
    internal sealed class AggregatedEventSummarizer : IEventSummarizer
    {
        private readonly EventSummarizer _summarizer = new EventSummarizer();

        public void SummarizeEvent(
            UnixMillisecondTime timestamp,
            string flagKey,
            int? flagVersion,
            int? variation,
            in LdValue value,
            in LdValue defaultValue,
            in Context context
            ) =>
            _summarizer.SummarizeEvent(timestamp, flagKey, flagVersion, variation, value, defaultValue, context);

        public IReadOnlyList<EventSummary> GetSummariesAndReset()
        {
            EventSummary summary = _summarizer.GetSummaryAndReset();
            return summary.Empty ? Array.Empty<EventSummary>() : new[] { summary };
        }

        public void Clear() => _summarizer.Clear();
    }
}
