using System.Collections.Generic;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;
using LaunchDarkly.Sdk.Server.Ai.Tracking;
using Moq;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Ai
{
    public class LdAiTrackerTest
    {
        private static bool MatchesTrackData(LdValue actual, string flagKey, LdAiConfig config)
        {
            return actual.Get("variationKey").Equals(LdValue.Of(config.VariationKey)) &&
                   actual.Get("version").Equals(LdValue.Of(config.Version)) &&
                   actual.Get("configKey").Equals(LdValue.Of(flagKey)) &&
                   actual.Get("modelName").Equals(LdValue.Of(config.Model.Name)) &&
                   actual.Get("providerName").Equals(LdValue.Of(config.Provider.Name)) &&
                   actual.Get("runId").Type == LdValueType.String &&
                   actual.Get("runId").AsString.Length > 0;
        }

        [Fact]
        public void ThrowsIfClientIsNull()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                new LdAiConfigTracker(null, "key", LdAiConfig.Disabled,  Context.New("key")));
        }

        [Fact]
        public void ThrowsIfConfigIsNull()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            Assert.Throws<System.ArgumentNullException>(() =>
                new LdAiConfigTracker(mockClient.Object, "key", null, Context.New("key")));
        }

        [Fact]
        public void ThrowsIfKeyIsNull()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            Assert.Throws<System.ArgumentNullException>(() =>
                new LdAiConfigTracker(mockClient.Object, null,  LdAiConfig.Disabled,  Context.New("key")));
        }

        [Fact]
        public void TrackDataIncludesRunId()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.Disabled;

            var tracker = new LdAiConfigTracker(mockClient.Object, flagKey, config, context);

            tracker.TrackDuration(1.0f);
            mockClient.Verify(x => x.Track("$ld:ai:duration:total", context,
                It.Is<LdValue>(d =>
                    d.Get("runId").Type == LdValueType.String &&
                    d.Get("runId").AsString.Length > 0),
                1.0f), Times.Once);
        }

        [Fact]
        public void CanTrackDuration()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.Disabled;
            var tracker = new LdAiConfigTracker(mockClient.Object, flagKey, config, context);

            tracker.TrackDuration(1.0f);
            mockClient.Verify(x => x.Track("$ld:ai:duration:total", context,
                It.Is<LdValue>(d => MatchesTrackData(d, flagKey, config)), 1.0f), Times.Once);
        }

        [Fact]
        public void CanTrackTimeToFirstToken()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.Disabled;
            var tracker = new LdAiConfigTracker(mockClient.Object, flagKey, config, context);

            tracker.TrackTimeToFirstToken(1.0f);
            mockClient.Verify(x => x.Track("$ld:ai:tokens:ttf", context,
                It.Is<LdValue>(d => MatchesTrackData(d, flagKey, config)), 1.0f), Times.Once);
        }

        [Fact]
        public void CanTrackSuccess()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.Disabled;

            var tracker = new LdAiConfigTracker(mockClient.Object, flagKey, config, context);
            tracker.TrackSuccess();
            mockClient.Verify(x => x.Track("$ld:ai:generation:success", context,
                It.Is<LdValue>(d => MatchesTrackData(d, flagKey, config)), 1.0f), Times.Once);
        }


        [Fact]
        public void CanTrackError()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.Disabled;

            var tracker = new LdAiConfigTracker(mockClient.Object, flagKey, config, context);
            tracker.TrackError();
            mockClient.Verify(x => x.Track("$ld:ai:generation:error", context,
                It.Is<LdValue>(d => MatchesTrackData(d, flagKey, config)), 1.0f), Times.Once);
        }


        [Fact]
        public async void CanTrackDurationOfTask()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.Disabled;

            var tracker = new LdAiConfigTracker(mockClient.Object, flagKey, config, context);


            const int waitMs = 10;

            var result = await tracker.TrackDurationOfTask(Task.Run(() =>
            {
                return Task.Delay(waitMs).ContinueWith(_ => "I waited");
            }));

            Assert.Equal("I waited", result);

            // The metricValue here is the duration of the task. Since the task waits for 10ms, we'll add a bit of
            // error so this isn't flaky. If this proves to be really flaky, we can at least constrain it to be
            // between 0 and some large number.
            mockClient.Verify(
                x => x.Track("$ld:ai:duration:total", context,
                    It.Is<LdValue>(d => MatchesTrackData(d, flagKey, config)),
                    It.IsInRange<double>(0, 500, Range.Inclusive)), Times.Once);
        }


        [Fact]
        public void CanTrackPositiveFeedback()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.Disabled;

            var tracker = new LdAiConfigTracker(mockClient.Object, flagKey, config, context);
            tracker.TrackFeedback(Feedback.Positive);

            mockClient.Verify(x => x.Track("$ld:ai:feedback:user:positive", context,
                It.Is<LdValue>(d => MatchesTrackData(d, flagKey, config)), 1.0f), Times.Once);
        }

        [Fact]
        public void CanTrackNegativeFeedback()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.Disabled;

            var tracker = new LdAiConfigTracker(mockClient.Object, flagKey, config, context);
            tracker.TrackFeedback(Feedback.Negative);

            mockClient.Verify(x => x.Track("$ld:ai:feedback:user:negative", context,
                It.Is<LdValue>(d => MatchesTrackData(d, flagKey, config)), 1.0f), Times.Once);
        }

        [Fact]
        public void CanTrackTokens()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.Disabled;

            var tracker = new LdAiConfigTracker(mockClient.Object, flagKey, config, context);

            var givenUsage = new Usage
            {
                Total = 1,
                Input = 2,
                Output = 3
            };

            tracker.TrackTokens(givenUsage);
            mockClient.Verify(x => x.Track("$ld:ai:tokens:total", context,
                It.Is<LdValue>(d => MatchesTrackData(d, flagKey, config)), 1.0f), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:tokens:input", context,
                It.Is<LdValue>(d => MatchesTrackData(d, flagKey, config)), 2.0f), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:tokens:output", context,
                It.Is<LdValue>(d => MatchesTrackData(d, flagKey, config)), 3.0f), Times.Once);
        }

        [Fact]
        public void CanTrackResponseWithSpecificLatency()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.Disabled;

            var tracker = new LdAiConfigTracker(mockClient.Object,  flagKey, config, context);

            var givenUsage = new Usage
            {
                Total = 1,
                Input = 2,
                Output = 3
            };

            var givenStatistics = new Metrics
            {
                LatencyMs = 500
            };

            var givenResponse = new Response
            {
                Usage = givenUsage,
                Metrics = givenStatistics
            };

            var result = tracker.TrackRequest(Task.Run(() => givenResponse));
            Assert.Equal(givenResponse, result.Result);
            mockClient.Verify(x => x.Track("$ld:ai:generation:success", context,
                It.Is<LdValue>(d => MatchesTrackData(d, flagKey, config)), 1.0f), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:tokens:total", context,
                It.Is<LdValue>(d => MatchesTrackData(d, flagKey, config)), 1.0f), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:tokens:input", context,
                It.Is<LdValue>(d => MatchesTrackData(d, flagKey, config)), 2.0f), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:tokens:output", context,
                It.Is<LdValue>(d => MatchesTrackData(d, flagKey, config)), 3.0f), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:duration:total", context,
                It.Is<LdValue>(d => MatchesTrackData(d, flagKey, config)), 500.0f), Times.Once);
        }

        [Fact]
        public void CanTrackResponseWithPartialData()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.Disabled;

            var tracker = new LdAiConfigTracker(mockClient.Object, flagKey, config, context);

            var givenUsage = new Usage
            {
                Total = 1
            };

            var givenResponse = new Response
            {
                Usage = givenUsage,
                Metrics = null
            };

            var result = tracker.TrackRequest(Task.Run(() => givenResponse));
            Assert.Equal(givenResponse, result.Result);
            mockClient.Verify(x => x.Track("$ld:ai:tokens:total", context,
                It.Is<LdValue>(d => MatchesTrackData(d, flagKey, config)), 1.0f), Times.Once);

            // if latency isn't provided via Statistics, then it is automatically measured.
            mockClient.Verify(x => x.Track("$ld:ai:duration:total", context,
                It.Is<LdValue>(d => MatchesTrackData(d, flagKey, config)), It.IsAny<double>()), Times.Once);
        }

        [Fact]
        public async Task CanTrackExceptionFromResponse()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.Disabled;

            var tracker = new LdAiConfigTracker(mockClient.Object, flagKey, config, context);

            await Assert.ThrowsAsync<System.Exception>(() => tracker.TrackRequest(Task.FromException<Response>(new System.Exception("I am an exception"))));

            mockClient.Verify(x => x.Track("$ld:ai:generation:error", context,
                It.Is<LdValue>(d => MatchesTrackData(d, flagKey, config)), 1.0f), Times.Once);

            // if latency isn't provided via Statistics, then it is automatically measured.
            mockClient.Verify(x => x.Track("$ld:ai:duration:total", context,
                It.Is<LdValue>(d => MatchesTrackData(d, flagKey, config)), It.IsAny<double>()), Times.Once);
        }

        [Fact]
        public void DuplicateTrackDurationIsIgnored()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.Disabled;

            var tracker = new LdAiConfigTracker(mockClient.Object, flagKey, config, context);

            tracker.TrackDuration(1.0f);
            tracker.TrackDuration(2.0f);

            mockClient.Verify(x => x.Track("$ld:ai:duration:total", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Once);
        }

        [Fact]
        public void DuplicateTrackTimeToFirstTokenIsIgnored()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.Disabled;

            var tracker = new LdAiConfigTracker(mockClient.Object, flagKey, config, context);

            tracker.TrackTimeToFirstToken(1.0f);
            tracker.TrackTimeToFirstToken(2.0f);

            mockClient.Verify(x => x.Track("$ld:ai:tokens:ttf", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Once);
        }

        [Fact]
        public void DuplicateTrackTokensIsIgnored()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.Disabled;

            var tracker = new LdAiConfigTracker(mockClient.Object, flagKey, config, context);

            var usage = new Usage { Total = 1, Input = 2, Output = 3 };

            tracker.TrackTokens(usage);
            tracker.TrackTokens(usage);

            mockClient.Verify(x => x.Track("$ld:ai:tokens:total", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:tokens:input", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:tokens:output", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Once);
        }

        [Fact]
        public void DuplicateTrackFeedbackIsIgnored()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.Disabled;

            var tracker = new LdAiConfigTracker(mockClient.Object, flagKey, config, context);

            tracker.TrackFeedback(Feedback.Positive);
            tracker.TrackFeedback(Feedback.Negative);

            mockClient.Verify(x => x.Track("$ld:ai:feedback:user:positive", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:feedback:user:negative", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Never);
        }

        [Fact]
        public void DuplicateTrackSuccessIsIgnored()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.Disabled;

            var tracker = new LdAiConfigTracker(mockClient.Object, flagKey, config, context);

            tracker.TrackSuccess();
            tracker.TrackSuccess();

            mockClient.Verify(x => x.Track("$ld:ai:generation:success", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Once);
        }

        [Fact]
        public void TrackErrorAfterSuccessIsIgnored()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.Disabled;

            var tracker = new LdAiConfigTracker(mockClient.Object, flagKey, config, context);

            tracker.TrackSuccess();
            tracker.TrackError();

            mockClient.Verify(x => x.Track("$ld:ai:generation:success", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:generation:error", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Never);
        }

        [Fact]
        public void TrackSuccessAfterErrorIsIgnored()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.Disabled;

            var tracker = new LdAiConfigTracker(mockClient.Object, flagKey, config, context);

            tracker.TrackError();
            tracker.TrackSuccess();

            mockClient.Verify(x => x.Track("$ld:ai:generation:error", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:generation:success", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Never);
        }
    }
}
