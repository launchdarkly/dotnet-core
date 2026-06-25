using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace LaunchDarkly.Sdk.Internal.Events
{
    public class PerContextEventSummarizerTest
    {
        private static readonly UnixMillisecondTime Time = UnixMillisecondTime.OfMillis(1000);
        private static readonly Context ContextA = Context.New(ContextKind.Of("user"), "a");
        private static readonly Context ContextB = Context.New(ContextKind.Of("user"), "b");

        private static EventSummary SummaryFor(IReadOnlyList<EventSummary> summaries, Context context) =>
            summaries.Single(s => s.Context.Equals(context));

        [Fact]
        public void EmptySummarizerReturnsNoSummaries()
        {
            var summarizer = new PerContextEventSummarizer();
            Assert.Empty(summarizer.GetSummariesAndReset());
        }

        [Fact]
        public void EachContextGetsItsOwnSummaryWithTheContextAttached()
        {
            var summarizer = new PerContextEventSummarizer();
            summarizer.SummarizeEvent(Time, "flag", 1, 0, LdValue.Of("a"), LdValue.Null, ContextA);
            summarizer.SummarizeEvent(Time, "flag", 1, 0, LdValue.Of("b"), LdValue.Null, ContextB);

            var summaries = summarizer.GetSummariesAndReset();

            Assert.Equal(2, summaries.Count);
            Assert.Equal(ContextA, SummaryFor(summaries, ContextA).Context);
            Assert.Equal(ContextB, SummaryFor(summaries, ContextB).Context);
        }

        [Fact]
        public void EvaluationsForEqualContextsAreMergedIntoOneSummary()
        {
            // Two distinct Context instances that are value-equal must map to the same accumulator.
            var contextA1 = Context.New(ContextKind.Of("user"), "a");
            var contextA2 = Context.New(ContextKind.Of("user"), "a");

            var summarizer = new PerContextEventSummarizer();
            summarizer.SummarizeEvent(Time, "flag", 1, 0, LdValue.Of("v"), LdValue.Null, contextA1);
            summarizer.SummarizeEvent(Time, "flag", 1, 0, LdValue.Of("v"), LdValue.Null, contextA2);

            var summaries = summarizer.GetSummariesAndReset();

            Assert.Single(summaries);
            var counter = summaries[0].Flags["flag"].Counters[new EventsCounterKey(1, 0)];
            Assert.Equal(2, counter.Count);
        }

        [Fact]
        public void ContextsDifferingByAttributeAreTrackedSeparately()
        {
            // Same key/kind but different attributes must be treated as different contexts.
            var plain = Context.New(ContextKind.Of("user"), "a");
            var withName = Context.Builder("a").Kind("user").Name("Pat").Build();

            var summarizer = new PerContextEventSummarizer();
            summarizer.SummarizeEvent(Time, "flag", 1, 0, LdValue.Of("v"), LdValue.Null, plain);
            summarizer.SummarizeEvent(Time, "flag", 1, 0, LdValue.Of("v"), LdValue.Null, withName);

            var summaries = summarizer.GetSummariesAndReset();

            Assert.Equal(2, summaries.Count);
        }

        [Fact]
        public void CountersAreAccumulatedPerContext()
        {
            var summarizer = new PerContextEventSummarizer();
            summarizer.SummarizeEvent(Time, "flag", 1, 0, LdValue.Of("a"), LdValue.Null, ContextA);
            summarizer.SummarizeEvent(Time, "flag", 1, 0, LdValue.Of("a"), LdValue.Null, ContextA);
            summarizer.SummarizeEvent(Time, "flag", 1, 0, LdValue.Of("b"), LdValue.Null, ContextB);

            var summaries = summarizer.GetSummariesAndReset();

            Assert.Equal(2, SummaryFor(summaries, ContextA).Flags["flag"].Counters[new EventsCounterKey(1, 0)].Count);
            Assert.Equal(1, SummaryFor(summaries, ContextB).Flags["flag"].Counters[new EventsCounterKey(1, 0)].Count);
        }

        [Fact]
        public void GetSummariesAndResetClearsState()
        {
            var summarizer = new PerContextEventSummarizer();
            summarizer.SummarizeEvent(Time, "flag", 1, 0, LdValue.Of("a"), LdValue.Null, ContextA);

            Assert.Single(summarizer.GetSummariesAndReset());
            Assert.Empty(summarizer.GetSummariesAndReset());
        }

        [Fact]
        public void ClearDiscardsAccumulatedData()
        {
            var summarizer = new PerContextEventSummarizer();
            summarizer.SummarizeEvent(Time, "flag", 1, 0, LdValue.Of("a"), LdValue.Null, ContextA);

            summarizer.Clear();

            Assert.Empty(summarizer.GetSummariesAndReset());
        }
    }
}
