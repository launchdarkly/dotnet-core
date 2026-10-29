using Xunit;

namespace LaunchDarkly.Sdk.Internal.Events
{
    public class AggregatedEventSummarizerTest
    {
        private static readonly UnixMillisecondTime Time = UnixMillisecondTime.OfMillis(1000);
        private static readonly Context ContextA = Context.New(ContextKind.Of("user"), "a");
        private static readonly Context ContextB = Context.New(ContextKind.Of("user"), "b");

        [Fact]
        public void EmptySummarizerReturnsNoSummaries()
        {
            var summarizer = new AggregatedEventSummarizer();
            Assert.Empty(summarizer.GetSummariesAndReset());
        }

        [Fact]
        public void AllContextsAreAggregatedIntoASingleContextlessSummary()
        {
            var summarizer = new AggregatedEventSummarizer();
            summarizer.SummarizeEvent(Time, "flag", 1, 0, LdValue.Of("a"), LdValue.Null, ContextA, false);
            summarizer.SummarizeEvent(Time, "flag", 1, 0, LdValue.Of("b"), LdValue.Null, ContextB, false);

            var summaries = summarizer.GetSummariesAndReset();

            Assert.Single(summaries);
            // No context is attached to an aggregated summary.
            Assert.False(summaries[0].Context.Defined);
            // Both evaluations are merged under the same flag/variation/version counter.
            Assert.Equal(2, summaries[0].Flags["flag"].Counters[new EventsCounterKey(1, 0)].Count);
        }

        [Fact]
        public void OverrideAffectedEvaluationsKeepTheirOwnCounter()
        {
            // The marker reaches the aggregated summary: marked evaluations are counted apart from
            // unmarked ones for the same flag, variation, and version, across contexts.
            var summarizer = new AggregatedEventSummarizer();
            summarizer.SummarizeEvent(Time, "flag", 1, 0, LdValue.Of("a"), LdValue.Null, ContextA, true);
            summarizer.SummarizeEvent(Time, "flag", 1, 0, LdValue.Of("a"), LdValue.Null, ContextB, true);
            summarizer.SummarizeEvent(Time, "flag", 1, 0, LdValue.Of("a"), LdValue.Null, ContextA, false);

            var counters = Assert.Single(summarizer.GetSummariesAndReset()).Flags["flag"].Counters;
            Assert.Equal(2, counters.Count);
            Assert.Equal(2, counters[new EventsCounterKey(1, 0, true)].Count);
            Assert.Equal(1, counters[new EventsCounterKey(1, 0, false)].Count);
        }

        [Fact]
        public void GetSummariesAndResetClearsState()
        {
            var summarizer = new AggregatedEventSummarizer();
            summarizer.SummarizeEvent(Time, "flag", 1, 0, LdValue.Of("a"), LdValue.Null, ContextA, false);

            Assert.Single(summarizer.GetSummariesAndReset());
            Assert.Empty(summarizer.GetSummariesAndReset());
        }
    }
}
