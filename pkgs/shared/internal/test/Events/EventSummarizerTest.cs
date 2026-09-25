using System.Collections.Generic;
using Xunit;

namespace LaunchDarkly.Sdk.Internal.Events
{
    public class EventSummarizerTest
    {
        private static Context _context = Context.New("key");

        [Fact]
        public void SummarizeEventSetsStartAndEndDates()
        {
            var time1 = UnixMillisecondTime.OfMillis(1000);
            var time2 = UnixMillisecondTime.OfMillis(2000);
            var time3 = UnixMillisecondTime.OfMillis(3000);
            EventSummarizer es = new EventSummarizer();
            es.SummarizeEvent(time2, "flag", null, null, LdValue.Null, LdValue.Null, _context, false);
            es.SummarizeEvent(time1, "flag", null, null, LdValue.Null, LdValue.Null, _context, false);
            es.SummarizeEvent(time3, "flag", null, null, LdValue.Null, LdValue.Null, _context, false);
            EventSummary data = es.GetSummaryAndReset();

            Assert.Equal(time1, data.StartDate);
            Assert.Equal(time3, data.EndDate);
        }

        [Fact]
        public void SummarizeEventIncrementsCounters()
        {
            var time = UnixMillisecondTime.OfMillis(1000);
            string flag1Key = "flag1", flag2Key = "flag2", unknownFlagKey = "badkey";
            int flag1Version = 100, flag2Version = 200;
            int variation1 = 1, variation2 = 2;
            LdValue value1 = LdValue.Of("value1"), value2 = LdValue.Of("value2"),
                value99 = LdValue.Of("value99"),
                default1 = LdValue.Of("default1"), default2 = LdValue.Of("default2"),
                default3 = LdValue.Of("default3");
            EventSummarizer es = new EventSummarizer();
            es.SummarizeEvent(time, flag1Key, flag1Version, variation1, value1, default1, _context, false);
            es.SummarizeEvent(time, flag1Key, flag1Version, variation2, value2, default1, _context, false);
            es.SummarizeEvent(time, flag2Key, flag2Version, variation1, value99, default2, _context, false);
            es.SummarizeEvent(time, flag1Key, flag1Version, variation1, value1, default1, _context, false);
            es.SummarizeEvent(time, unknownFlagKey, null, null, default3, default3, _context, false);
            EventSummary data = es.GetSummaryAndReset();

            Dictionary<EventsCounterKey, EventsCounterValue> expected = new Dictionary<EventsCounterKey, EventsCounterValue>();
            Assert.Equal(new EventsCounterValue(2, value1),
                data.Flags[flag1Key].Counters[new EventsCounterKey(flag1Version, variation1)]);
            Assert.Equal(new EventsCounterValue(1, value2),
                data.Flags[flag1Key].Counters[new EventsCounterKey(flag1Version, variation2)]);
            Assert.Equal(new EventsCounterValue(1, value99),
                data.Flags[flag2Key].Counters[new EventsCounterKey(flag2Version, variation1)]);
            Assert.Equal(new EventsCounterValue(1, default3),
                data.Flags[unknownFlagKey].Counters[new EventsCounterKey(null, null)]);
        }

        [Fact]
        public void OverrideAffectedEvaluationsAreCountedSeparately()
        {
            var time = UnixMillisecondTime.OfMillis(1000);
            var value = LdValue.Of("value");
            var defaultValue = LdValue.Of("default");
            EventSummarizer es = new EventSummarizer();
            es.SummarizeEvent(time, "flag", 11, 1, value, defaultValue, _context, false);
            es.SummarizeEvent(time, "flag", 11, 1, value, defaultValue, _context, true);
            es.SummarizeEvent(time, "flag", 11, 1, value, defaultValue, _context, true);
            EventSummary data = es.GetSummaryAndReset();

            var counters = data.Flags["flag"].Counters;
            Assert.Equal(2, counters.Count);
            Assert.Equal(new EventsCounterValue(1, value), counters[new EventsCounterKey(11, 1, false)]);
            Assert.Equal(new EventsCounterValue(2, value), counters[new EventsCounterKey(11, 1, true)]);
        }

        [Fact]
        public void CounterKeyEqualityIncludesTheOverrideAffectedMarker()
        {
            Assert.Equal(new EventsCounterKey(11, 1), new EventsCounterKey(11, 1, false));
            Assert.NotEqual(new EventsCounterKey(11, 1, false), new EventsCounterKey(11, 1, true));
            Assert.NotEqual(new EventsCounterKey(11, 1, false).GetHashCode(), new EventsCounterKey(11, 1, true).GetHashCode());
        }

        [Fact]
        public void SummarizeEventRemembersContextKinds()
        {
            var time = UnixMillisecondTime.OfMillis(1000);
            string flag1Key = "flag1", flag2Key = "flag2", flag3Key = "flag3";
            int version = 100, variation = 1;
            LdValue value = LdValue.Of("irrelevant");

            var c1 = Context.New(ContextKind.Of("kind1"), "key1");
            var c2 = Context.New(ContextKind.Of("kind2"), "key2");
            var c3 = Context.New(ContextKind.Of("kind3"), "key3");
            var multi = Context.NewMulti(c1, c2, c3);

            EventSummarizer es = new EventSummarizer();
            // flag1 gets only kind1; flag2 gets kind1 and kind2; flag3 gets kind1, kind2, and kind3
            es.SummarizeEvent(time, flag1Key, version, variation, value, value, c1, false);
            es.SummarizeEvent(time, flag2Key, version, variation, value, value, c1, false);
            es.SummarizeEvent(time, flag2Key, version, variation, value, value, c2, false);
            es.SummarizeEvent(time, flag3Key, version, variation, value, value, multi, false);
            EventSummary data = es.GetSummaryAndReset();

            Assert.Equal(new HashSet<string> { "kind1" }, data.Flags[flag1Key].ContextKinds);
            Assert.Equal(new HashSet<string> { "kind1", "kind2" }, data.Flags[flag2Key].ContextKinds);
            Assert.Equal(new HashSet<string> { "kind1", "kind2", "kind3" }, data.Flags[flag3Key].ContextKinds);
        }
    }
}
