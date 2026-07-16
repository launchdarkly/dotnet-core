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
            summarizer.SummarizeEvent(Time, "flag", 1, 0, LdValue.Of("a"), LdValue.Null, ContextA);
            summarizer.SummarizeEvent(Time, "flag", 1, 0, LdValue.Of("b"), LdValue.Null, ContextB);

            var summaries = summarizer.GetSummariesAndReset();

            Assert.Single(summaries);
            // No context is attached to an aggregated summary.
            Assert.False(summaries[0].Context.Defined);
            // Both evaluations are merged under the same flag/variation/version counter.
            Assert.Equal(2, summaries[0].Flags["flag"].Counters[new EventsCounterKey(1, 0)].Count);
        }

        [Fact]
        public void GetSummariesAndResetClearsState()
        {
            var summarizer = new AggregatedEventSummarizer();
            summarizer.SummarizeEvent(Time, "flag", 1, 0, LdValue.Of("a"), LdValue.Null, ContextA);

            Assert.Single(summarizer.GetSummariesAndReset());
            Assert.Empty(summarizer.GetSummariesAndReset());
        }
    }
}
