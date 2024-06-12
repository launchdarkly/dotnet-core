using System;
using LaunchDarkly.Sdk.Client.Integrations;
using LaunchDarkly.Sdk.Client.Interfaces;
using LaunchDarkly.TestHelpers;
using Xunit;
using Xunit.Abstractions;

namespace LaunchDarkly.Sdk.Client
{
    public class LdClientListenersTest : BaseTest
    {
        // Tests for data source status listeners are in LdClientDataSourceStatusTests.

        public LdClientListenersTest(ITestOutputHelper testOutput) : base(testOutput) { }

        [Fact]
        public void ClientSendsFlagValueChangeEvents()
        {
            var testData = TestData.DataSource();
            var config = BasicConfig().DataSource(testData).Build();

            var flagKey = "flagkey";
            testData.Update(testData.Flag(flagKey).Variation(true));

            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                var eventSink1 = new EventSink<FlagValueChangeEvent>();
                var eventSink2 = new EventSink<FlagValueChangeEvent>();
                EventHandler<FlagValueChangeEvent> listener1 = eventSink1.Add;
                EventHandler<FlagValueChangeEvent> listener2 = eventSink2.Add;
                client.FlagTracker.FlagValueChanged += listener1;
                client.FlagTracker.FlagValueChanged += listener2;

                eventSink1.ExpectNoValue();
                eventSink2.ExpectNoValue();

                testData.Update(testData.Flag(flagKey).Variation(false));

                var event1 = eventSink1.ExpectValue();
                var event2 = eventSink2.ExpectValue();
                Assert.Equal(flagKey, event1.Key);
                Assert.Equal(LdValue.Of(true), event1.OldValue);
                Assert.Equal(LdValue.Of(false), event1.NewValue);
                Assert.Equal(event1, event2);

                eventSink1.ExpectNoValue();
                eventSink2.ExpectNoValue();
            }
        }

        [Fact]
        public void EventSenderIsClientInstance()
        {
            // We're only checking one kind of events here (FlagValueChanged), but since the SDK uses the
            // same TaskExecutor instance for all event dispatches and the sender is configured in
            // that object, the sender should be the same for all events.

            var flagKey = "flagKey";
            var testData = TestData.DataSource();
            testData.Update(testData.Flag(flagKey).Variation(true));
            var config = BasicConfig().DataSource(testData).Build();

            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                var receivedSender = new EventSink<object>();
                client.FlagTracker.FlagValueChanged += (s, e) => receivedSender.Enqueue(s);

                testData.Update(testData.Flag(flagKey).Variation(false));

                var sender = receivedSender.ExpectValue();
                Assert.Same(client, sender);
            }
        }
    }
}
