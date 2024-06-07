using System;
using System.Collections.Generic;
using LaunchDarkly.TestHelpers;
using Xunit;
using Xunit.Abstractions;

namespace LaunchDarkly.Sdk.Client.Integrations
{
    public class EventProcessorBuilderTest : BaseTest
    {
        private readonly BuilderBehavior.InternalStateTester<EventProcessorBuilder> _tester =
            BuilderBehavior.For(Components.SendEvents);

        public EventProcessorBuilderTest(ITestOutputHelper testOutput) : base(testOutput) { }

        [Fact]
        public void AllAttributesPrivate()
        {
            var prop = _tester.Property(b => b._allAttributesPrivate, (b, v) => b.AllAttributesPrivate(v));
            prop.AssertDefault(false);
            prop.AssertCanSet(true);
        }

        [Fact]
        public void DiagnosticRecordingInterval()
        {
            var prop = _tester.Property(b => b._diagnosticRecordingInterval, (b, v) => b.DiagnosticRecordingInterval(v));
            prop.AssertDefault(EventProcessorBuilder.DefaultDiagnosticRecordingInterval);
            prop.AssertCanSet(TimeSpan.FromMinutes(7));
            prop.AssertSetIsChangedTo(TimeSpan.FromMinutes(4), EventProcessorBuilder.MinimumDiagnosticRecordingInterval);
            prop.AssertSetIsChangedTo(TimeSpan.FromMilliseconds(-1), EventProcessorBuilder.MinimumDiagnosticRecordingInterval);
        }

        [Fact]
        public void EventCapacity()
        {
            var prop = _tester.Property(b => b._capacity, (b, v) => b.Capacity(v));
            prop.AssertDefault(EventProcessorBuilder.DefaultCapacity);
            prop.AssertCanSet(1);
            prop.AssertSetIsChangedTo(0, EventProcessorBuilder.DefaultCapacity);
            prop.AssertSetIsChangedTo(-1, EventProcessorBuilder.DefaultCapacity);
        }

        [Fact]
        public void FlushInterval()
        {
            var prop = _tester.Property(b => b._flushInterval, (b, v) => b.FlushInterval(v));
            prop.AssertDefault(EventProcessorBuilder.DefaultFlushInterval);
            prop.AssertCanSet(TimeSpan.FromMinutes(7));
            prop.AssertSetIsChangedTo(TimeSpan.Zero, EventProcessorBuilder.DefaultFlushInterval);
            prop.AssertSetIsChangedTo(TimeSpan.FromMilliseconds(-1), EventProcessorBuilder.DefaultFlushInterval);
        }

        [Fact]
        public void PrivateAttributes()
        {
            var b = _tester.New();
            Assert.Empty(b._privateAttributes);
            b.PrivateAttributes("name");
            b.PrivateAttributes("/address/street", "other");
            Assert.Equal(
                new HashSet<AttributeRef>
                {
                    AttributeRef.FromPath("name"), AttributeRef.FromPath("/address/street"), AttributeRef.FromPath("other")
                },
                b._privateAttributes);
        }
    }
}