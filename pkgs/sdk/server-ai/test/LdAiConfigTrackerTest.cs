using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
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
        private static LdAiCompletionConfig BuildConfig(ILaunchDarklyClient client, string configKey, Context context)
        {
            // Construct a disabled LdAiCompletionConfig with a tracker factory that produces the
            // tracker under test. Using the internal constructor here is acceptable because the
            // SDK exposes its internals to this test assembly via InternalsVisibleTo.
            return new LdAiCompletionConfig(
                key: configKey,
                enabled: false,
                variationKey: "",
                version: 1,
                messages: new List<LdAiConfigTypes.Message>(),
                tools: new Dictionary<string, LdAiConfigTypes.Tool>(),
                judgeConfiguration: null,
                model: null,
                provider: null,
                trackerFactory: cfg => new LdAiConfigTracker(
                    client,
                    Guid.NewGuid().ToString(),
                    cfg.Key,
                    cfg.VariationKey,
                    cfg.Version,
                    context,
                    cfg.Model?.Name,
                    cfg.Provider?.Name));
        }

        // The track data carries the per-execution runId, which is generated at tracker
        // construction time and is therefore unknown to the test. This helper validates the
        // stable fields by value and asserts only that runId is a non-empty string.
        // variationKey is omitted from track data when empty, so the helper handles that
        // by comparing against LdValue.Null in that case.
        private static bool MatchesTrackData(LdValue actual, string flagKey, LdAiConfig config)
        {
            var variationKeyMatch = string.IsNullOrEmpty(config.VariationKey)
                ? actual.Get("variationKey").IsNull
                : actual.Get("variationKey").Equals(LdValue.Of(config.VariationKey));

            return variationKeyMatch &&
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
            // The tracker's single internal ctor validates the client. Use representative
            // valid values for the other args.
            Assert.Throws<System.ArgumentNullException>(() =>
                new LdAiConfigTracker(null, Guid.NewGuid().ToString(), "key",
                    "", 1, Context.New("key"), "", ""));
        }

        [Fact]
        public void ThrowsIfConfigKeyIsNull()
        {
            // The field-based ctor must validate that configKey is non-null so misuse
            // fails fast at construction time rather than on the first Track call.
            var mockClient = new Mock<ILaunchDarklyClient>();
            Assert.Throws<ArgumentNullException>(() =>
                new LdAiConfigTracker(mockClient.Object, Guid.NewGuid().ToString(), null,
                    "", 1, Context.New("key"), "", ""));
        }

        [Fact]
        public void AcceptsConfigViaBaseTypeForModeAgnosticConstruction()
        {
            // The trackerFactory takes LdAiConfig so future agent / judge config types
            // can produce trackers via the same factory. Verify that upcasting a concrete
            // LdAiCompletionConfig to its base still flows through to a working tracker.
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";

            LdAiConfig configAsBase = BuildConfig(mockClient.Object, flagKey, context);
            var tracker = configAsBase.CreateTracker();

            tracker.TrackSuccess();
            mockClient.Verify(x => x.Track(
                "$ld:ai:generation:success",
                context,
                It.Is<LdValue>(v => v.Get("configKey").AsString == flagKey),
                1.0f), Times.Once);
        }

        [Fact]
        public void TrackDataIncludesRunId()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = BuildConfig(mockClient.Object, flagKey, context);

            var tracker = config.CreateTracker();

            tracker.TrackDuration(1.0);
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
            var config = BuildConfig(mockClient.Object, flagKey, context);
            var tracker = config.CreateTracker();

            tracker.TrackDuration(1.0);
            mockClient.Verify(x => x.Track("$ld:ai:duration:total", context,
                It.Is<LdValue>(d => MatchesTrackData(d, flagKey, config)), 1.0f), Times.Once);
        }

        [Fact]
        public void CanTrackTimeToFirstToken()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = BuildConfig(mockClient.Object, flagKey, context);
            var tracker = config.CreateTracker();

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
            var config = BuildConfig(mockClient.Object, flagKey, context);

            var tracker = config.CreateTracker();
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
            var config = BuildConfig(mockClient.Object, flagKey, context);

            var tracker = config.CreateTracker();
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
            var config = BuildConfig(mockClient.Object, flagKey, context);

            var tracker = config.CreateTracker();


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
                    It.IsInRange<double>(0, 500, Moq.Range.Inclusive)), Times.Once);
        }


        [Fact]
        public void CanTrackPositiveFeedback()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = BuildConfig(mockClient.Object, flagKey, context);

            var tracker = config.CreateTracker();
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
            var config = BuildConfig(mockClient.Object, flagKey, context);

            var tracker = config.CreateTracker();
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
            var config = BuildConfig(mockClient.Object, flagKey, context);

            var tracker = config.CreateTracker();

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
            var config = BuildConfig(mockClient.Object, flagKey, context);

            var tracker = config.CreateTracker();

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
            var config = BuildConfig(mockClient.Object, flagKey, context);

            var tracker = config.CreateTracker();

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
            var config = BuildConfig(mockClient.Object, flagKey, context);

            var tracker = config.CreateTracker();

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
            var config = BuildConfig(mockClient.Object, flagKey, context);

            var tracker = config.CreateTracker();

            tracker.TrackDuration(1.0);
            tracker.TrackDuration(2.0);

            mockClient.Verify(x => x.Track("$ld:ai:duration:total", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Once);
        }

        [Fact]
        public void DuplicateTrackTimeToFirstTokenIsIgnored()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = BuildConfig(mockClient.Object, flagKey, context);

            var tracker = config.CreateTracker();

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
            var config = BuildConfig(mockClient.Object, flagKey, context);

            var tracker = config.CreateTracker();

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
        public void TrackTokensWithEmptyUsageDoesNotConsumeAtMostOnceSlot()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";

            var config = BuildConfig(mockClient.Object, flagKey, context);

            var tracker = config.CreateTracker();

            // Empty usage emits no events.
            tracker.TrackTokens(default(Usage));

            mockClient.Verify(x => x.Track("$ld:ai:tokens:total", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Never);
            mockClient.Verify(x => x.Track("$ld:ai:tokens:input", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Never);
            mockClient.Verify(x => x.Track("$ld:ai:tokens:output", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Never);
            Assert.Null(tracker.Summary.Tokens);

            // A subsequent call with positive values should still record.
            var realUsage = new Usage { Total = 100, Input = 40, Output = 60 };
            tracker.TrackTokens(realUsage);

            mockClient.Verify(x => x.Track("$ld:ai:tokens:total", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:tokens:input", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:tokens:output", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Once);

            // And the slot is now consumed.
            Assert.Equal(realUsage, tracker.Summary.Tokens);
        }

        [Fact]
        public void DuplicateTrackFeedbackIsIgnored()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = BuildConfig(mockClient.Object, flagKey, context);

            var tracker = config.CreateTracker();

            tracker.TrackFeedback(Feedback.Positive);
            tracker.TrackFeedback(Feedback.Negative);

            mockClient.Verify(x => x.Track("$ld:ai:feedback:user:positive", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:feedback:user:negative", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Never);
        }

        [Fact]
        public void TrackFeedbackWithInvalidValueDoesNotConsumeAtMostOnceSlot()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";

            var config = BuildConfig(mockClient.Object, flagKey, context);
            var tracker = config.CreateTracker();

            Assert.Throws<ArgumentOutOfRangeException>(() => tracker.TrackFeedback((Feedback)42));

            // No event emitted by the invalid call.
            mockClient.Verify(x => x.Track("$ld:ai:feedback:user:positive", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Never);
            mockClient.Verify(x => x.Track("$ld:ai:feedback:user:negative", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Never);

            // A subsequent valid call should record normally.
            tracker.TrackFeedback(Feedback.Positive);
            mockClient.Verify(x => x.Track("$ld:ai:feedback:user:positive", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Once);

            // And the slot is now consumed.
            Assert.Equal(Feedback.Positive, tracker.Summary.Feedback);
        }

        [Fact]
        public void DuplicateTrackSuccessIsIgnored()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = BuildConfig(mockClient.Object, flagKey, context);

            var tracker = config.CreateTracker();

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
            var config = BuildConfig(mockClient.Object, flagKey, context);

            var tracker = config.CreateTracker();

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
            var config = BuildConfig(mockClient.Object, flagKey, context);

            var tracker = config.CreateTracker();

            tracker.TrackError();
            tracker.TrackSuccess();

            mockClient.Verify(x => x.Track("$ld:ai:generation:error", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:generation:success", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Never);
        }

        [Fact]
        public void ConfigCreateTrackerReturnsTrackerWhenDisabled()
        {
            // A disabled config still produces a working tracker. This guarantees the caller
            // can always emit events without null-checking, even when LaunchDarkly's served
            // config is disabled.
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            var config = BuildConfig(mockClient.Object, "key", context);
            Assert.False(config.Enabled);

            var tracker = config.CreateTracker();
            Assert.NotNull(tracker);

            tracker.TrackSuccess();
            mockClient.Verify(x => x.Track("$ld:ai:generation:success", context,
                It.IsAny<LdValue>(), 1.0f), Times.Once);
        }

        [Fact]
        public void ConfigCreateTrackerReturnsNewTrackerWithFreshRunId()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = BuildConfig(mockClient.Object, flagKey, context);

            var tracker1 = config.CreateTracker();
            var tracker2 = config.CreateTracker();

            tracker1.TrackDuration(10.0);
            tracker2.TrackDuration(20.0);

            string runId1 = null;
            string runId2 = null;

            foreach (var call in mockClient.Invocations)
            {
                if (call.Method.Name == "Track" && (string)call.Arguments[0] == "$ld:ai:duration:total")
                {
                    var data = (LdValue)call.Arguments[2];
                    var runId = data.Get("runId").AsString;
                    if (runId1 == null) runId1 = runId;
                    else runId2 = runId;
                }
            }

            Assert.NotNull(runId1);
            Assert.NotNull(runId2);
            Assert.NotEqual(runId1, runId2);
        }

        [Fact]
        public void ConfigCreateTrackerReturnsTrackerWithIndependentTrackingState()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = BuildConfig(mockClient.Object, flagKey, context);

            // Track duration on the first tracker (exhausts at-most-once)
            var tracker1 = config.CreateTracker();
            tracker1.TrackDuration(1.0);

            // A second tracker has fresh tracking state
            var tracker2 = config.CreateTracker();
            tracker2.TrackDuration(2.0);

            // Both calls should have gone through
            mockClient.Verify(x => x.Track("$ld:ai:duration:total", context,
                It.IsAny<LdValue>(), 1.0f), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:duration:total", context,
                It.IsAny<LdValue>(), 2.0f), Times.Once);
        }

        [Fact]
        public void ConfigCreateTrackerCanBeCalledMultipleTimes()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = BuildConfig(mockClient.Object, flagKey, context);

            var tracker1 = config.CreateTracker();
            var tracker2 = config.CreateTracker();
            var tracker3 = config.CreateTracker();

            Assert.NotNull(tracker1);
            Assert.NotNull(tracker2);
            Assert.NotNull(tracker3);

            // Each tracker should be able to independently track success
            tracker1.TrackSuccess();
            tracker2.TrackSuccess();
            tracker3.TrackSuccess();

            mockClient.Verify(x => x.Track("$ld:ai:generation:success", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Exactly(3));
        }

        [Fact]
        public void ResumptionTokenContainsExpectedFields()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "my-config-key";
            var config = BuildConfig(mockClient.Object, flagKey, context);
            var tracker = config.CreateTracker();
            var token = tracker.ResumptionToken;

            Assert.NotNull(token);
            Assert.NotEmpty(token);

            // Decode and verify the payload
            var base64 = token.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            var doc = JsonDocument.Parse(json);

            Assert.Equal(flagKey, doc.RootElement.GetProperty("configKey").GetString());
            Assert.Equal(config.Version, doc.RootElement.GetProperty("version").GetInt32());
            Assert.True(doc.RootElement.GetProperty("runId").GetString().Length > 0);

            // variationKey is empty, so it should be omitted
            Assert.False(doc.RootElement.TryGetProperty("variationKey", out _));

            // modelName and providerName should NOT be in the token
            Assert.False(doc.RootElement.TryGetProperty("modelName", out _));
            Assert.False(doc.RootElement.TryGetProperty("providerName", out _));
        }

        [Fact]
        public void ResumptionTokenHasCanonicalKeyOrder()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");

            var tracker = new LdAiConfigTracker(mockClient.Object, "test-run-id",
                "my-config-key", "my-variation", 5, context, "", "");
            var token = tracker.ResumptionToken;

            var base64 = token.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));

            // Verify canonical order: runId → configKey → variationKey → version
            var runIdIdx = json.IndexOf("\"runId\"", StringComparison.Ordinal);
            var configKeyIdx = json.IndexOf("\"configKey\"", StringComparison.Ordinal);
            var variationKeyIdx = json.IndexOf("\"variationKey\"", StringComparison.Ordinal);
            var versionIdx = json.IndexOf("\"version\"", StringComparison.Ordinal);

            Assert.True(runIdIdx < configKeyIdx);
            Assert.True(configKeyIdx < variationKeyIdx);
            Assert.True(variationKeyIdx < versionIdx);
        }

        [Fact]
        public void ResumptionTokenIsUrlSafeBase64()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            var config = BuildConfig(mockClient.Object, "key", context);

            var tracker = config.CreateTracker();
            var token = tracker.ResumptionToken;

            // URL-safe base64 should not contain +, /, or =
            Assert.DoesNotContain("+", token);
            Assert.DoesNotContain("/", token);
            Assert.DoesNotContain("=", token);
        }

        [Fact]
        public void ResumptionTokenIsConsistentAcrossCalls()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            var config = BuildConfig(mockClient.Object, "key", context);

            var tracker = config.CreateTracker();

            var token1 = tracker.ResumptionToken;
            var token2 = tracker.ResumptionToken;

            Assert.Equal(token1, token2);
        }

        [Fact]
        public void ResumptionTokenIncludesVariationKeyWhenPresent()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var mockLogger = new Mock<ILogger>();
            mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);
            var context = Context.New("key");

            // Use the LdAiClient to get a config with a real variationKey from flag evaluation
            mockClient.Setup(x =>
                x.JsonVariation("key", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(
                LdValue.ObjectFrom(new Dictionary<string, LdValue>
                {
                    ["_ldMeta"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
                    {
                        ["enabled"] = LdValue.Of(true),
                        ["variationKey"] = LdValue.Of("my-variation"),
                        ["version"] = LdValue.Of(5)
                    }),
                    ["messages"] = LdValue.ArrayOf()
                }));

            var client = new LdAiClient(mockClient.Object);
            var config = client.CompletionConfig("key", context);
            var tracker = config.CreateTracker();
            var token = tracker.ResumptionToken;

            // Decode and verify variationKey is present
            var base64 = token.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            var doc = JsonDocument.Parse(json);

            Assert.Equal("my-variation", doc.RootElement.GetProperty("variationKey").GetString());
        }

        [Fact]
        public void FromResumptionTokenReconstructsTrackerWithOriginalRunId()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            var config = BuildConfig(mockClient.Object, "my-key", context);

            var original = config.CreateTracker();
            var token = original.ResumptionToken;

            var newContext = Context.New("other-key");
            var resumed = LdAiConfigTracker.FromResumptionToken(token, mockClient.Object, newContext);

            // Both should track with the same runId
            original.TrackDuration(10);
            resumed.TrackDuration(20);

            string originalRunId = null;
            string resumedRunId = null;
            foreach (var call in mockClient.Invocations)
            {
                if (call.Method.Name == "Track" && (string)call.Arguments[0] == "$ld:ai:duration:total")
                {
                    var data = (LdValue)call.Arguments[2];
                    if (originalRunId == null) originalRunId = data.Get("runId").AsString;
                    else resumedRunId = data.Get("runId").AsString;
                }
            }

            Assert.NotNull(originalRunId);
            Assert.Equal(originalRunId, resumedRunId);
        }

        [Fact]
        public void FromResumptionTokenThrowsOnNullToken()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            Assert.Throws<ArgumentNullException>(() =>
                LdAiConfigTracker.FromResumptionToken(null, mockClient.Object, Context.New("key")));
        }

        [Fact]
        public void FromResumptionTokenThrowsOnInvalidToken()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            Assert.Throws<ArgumentException>(() =>
                LdAiConfigTracker.FromResumptionToken("not-valid!!!", mockClient.Object, Context.New("key")));
        }

        [Fact]
        public void FromResumptionTokenRejectsNullJsonPayload()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();

            // Base64-encode the JSON literal "null" so deserialization succeeds and yields a null payload.
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("null"));
            var token = base64.Replace('+', '-').Replace('/', '_').TrimEnd('=');

            Assert.Throws<ArgumentException>(() =>
                LdAiConfigTracker.FromResumptionToken(token, mockClient.Object, Context.New("key")));
        }

        [Fact]
        public void SummaryReflectsTrackedMetrics()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");

            var config = BuildConfig(mockClient.Object, "key", context);
            var tracker = config.CreateTracker();

            // Initially all null
            var summary = tracker.Summary;
            Assert.Null(summary.DurationMs);
            Assert.Null(summary.Feedback);
            Assert.Null(summary.Tokens);
            Assert.Null(summary.Success);
            Assert.Null(summary.TimeToFirstTokenMs);

            // Track some metrics
            tracker.TrackDuration(100.5);
            tracker.TrackTimeToFirstToken(10.2f);
            tracker.TrackFeedback(Feedback.Positive);
            tracker.TrackSuccess();
            tracker.TrackTokens(new Usage { Total = 10, Input = 3, Output = 7 });

            summary = tracker.Summary;
            Assert.Equal(100.5, summary.DurationMs.Value, 1);
            Assert.Equal(Feedback.Positive, summary.Feedback);
            Assert.NotNull(summary.Tokens);
            Assert.Equal(10, summary.Tokens.Value.Total);
            Assert.Equal(3, summary.Tokens.Value.Input);
            Assert.Equal(7, summary.Tokens.Value.Output);
            Assert.True(summary.Success);
            Assert.Equal(10.2, summary.TimeToFirstTokenMs.Value, 1);
        }

        [Fact]
        public void SummaryReflectsErrorState()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");

            var config = BuildConfig(mockClient.Object, "key", context);
            var tracker = config.CreateTracker();
            tracker.TrackError();

            var summary = tracker.Summary;
            Assert.False(summary.Success);
        }

        [Fact]
        public void TrackSuccessIsAtMostOnceUnderConcurrency()
        {
            const int iterations = 500;
            var maxObservedInvocations = 0;
            var iterationWithMax = -1;

            for (var i = 0; i < iterations; i++)
            {
                var mockClient = new Mock<ILaunchDarklyClient>();
                var context = Context.New("key");
                var config = BuildConfig(mockClient.Object, "key", context);
                var tracker = config.CreateTracker();

                var barrier = new Barrier(2);
                var t1 = Task.Run(() => { barrier.SignalAndWait(); tracker.TrackSuccess(); });
                var t2 = Task.Run(() => { barrier.SignalAndWait(); tracker.TrackSuccess(); });
                Task.WaitAll(t1, t2);

                var calls = mockClient.Invocations.Count(inv =>
                    inv.Method.Name == "Track" &&
                    (string)inv.Arguments[0] == "$ld:ai:generation:success");

                if (calls > maxObservedInvocations) { maxObservedInvocations = calls; iterationWithMax = i; }
            }

            Assert.True(maxObservedInvocations == 1,
                $"Expected at-most-once emission of $ld:ai:generation:success across {iterations} " +
                $"iterations, but observed {maxObservedInvocations} emissions on iteration {iterationWithMax}. " +
                "TrackSuccess's check-then-set on _trackedSuccess is not atomic.");
        }

        [Fact]
        public void TrackSuccessAndTrackErrorAreMutuallyExclusiveUnderConcurrency()
        {
            const int iterations = 500;
            var iterationsWithBoth = 0;

            for (var i = 0; i < iterations; i++)
            {
                var mockClient = new Mock<ILaunchDarklyClient>();
                var context = Context.New("key");
                var config = BuildConfig(mockClient.Object, "key", context);
                var tracker = config.CreateTracker();

                var barrier = new Barrier(2);
                var t1 = Task.Run(() => { barrier.SignalAndWait(); tracker.TrackSuccess(); });
                var t2 = Task.Run(() => { barrier.SignalAndWait(); tracker.TrackError(); });
                Task.WaitAll(t1, t2);

                var successCalls = mockClient.Invocations.Count(inv =>
                    inv.Method.Name == "Track" && (string)inv.Arguments[0] == "$ld:ai:generation:success");
                var errorCalls = mockClient.Invocations.Count(inv =>
                    inv.Method.Name == "Track" && (string)inv.Arguments[0] == "$ld:ai:generation:error");

                if (successCalls > 0 && errorCalls > 0) iterationsWithBoth++;
            }

            Assert.True(iterationsWithBoth == 0,
                $"Expected TrackSuccess and TrackError to be mutually exclusive across {iterations} " +
                $"iterations, but {iterationsWithBoth} iterations emitted both success AND error " +
                "events for the same runId. The shared _trackedSuccess slot is not atomically guarded.");
        }

        [Fact]
        public void TrackDurationIsAtMostOnceUnderConcurrency()
        {
            const int iterations = 500;
            var maxObservedInvocations = 0;
            var iterationWithMax = -1;

            for (var i = 0; i < iterations; i++)
            {
                var mockClient = new Mock<ILaunchDarklyClient>();
                var context = Context.New("key");
                var config = BuildConfig(mockClient.Object, "key", context);
                var tracker = config.CreateTracker();

                var barrier = new Barrier(2);
                var t1 = Task.Run(() => { barrier.SignalAndWait(); tracker.TrackDuration(123.0); });
                var t2 = Task.Run(() => { barrier.SignalAndWait(); tracker.TrackDuration(123.0); });
                Task.WaitAll(t1, t2);

                var calls = mockClient.Invocations.Count(inv =>
                    inv.Method.Name == "Track" &&
                    (string)inv.Arguments[0] == "$ld:ai:duration:total");

                if (calls > maxObservedInvocations) { maxObservedInvocations = calls; iterationWithMax = i; }
            }

            Assert.True(maxObservedInvocations == 1,
                $"Expected at-most-once emission of $ld:ai:duration:total across {iterations} " +
                $"iterations, but observed {maxObservedInvocations} emissions on iteration {iterationWithMax}. " +
                "TrackDuration's check-then-set on _durationMs is not atomic.");
        }

        // ── TrackDurationOf ─────────────────────────────────────────────────

        [Fact]
        public async Task TrackDurationOf_MeasuresDuration()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var mockLogger = new Mock<ILogger>();
            mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);
            var context = Context.New("key");
            var config = BuildConfig(mockClient.Object, "flag", context);
            var tracker = config.CreateTracker();

            await tracker.TrackDurationOf(async () =>
            {
                await Task.Delay(50);
                return 42;
            });

            mockClient.Verify(x => x.Track(
                "$ld:ai:duration:total",
                context,
                It.IsAny<LdValue>(),
                It.Is<double>(ms => ms >= 40 && ms < 5000)), Times.Once);
        }

        // ── TrackMetricsOf ──────────────────────────────────────────────────

        [Fact]
        public async Task TrackMetricsOf_SuccessPath_TracksAllMetrics()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var mockLogger = new Mock<ILogger>();
            mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);
            var context = Context.New("key");
            var config = BuildConfig(mockClient.Object, "flag", context);
            var tracker = config.CreateTracker();

            var result = await tracker.TrackMetricsOf(
                _ => new AiMetrics(true, new Usage(10, 5, 5)),
                () => Task.FromResult("response"));

            Assert.Equal("response", result);

            mockClient.Verify(x => x.Track("$ld:ai:duration:total", context, It.IsAny<LdValue>(),
                It.IsAny<double>()), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:generation:success", context, It.IsAny<LdValue>(),
                1.0), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:tokens:total", context, It.IsAny<LdValue>(),
                10.0), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:tokens:input", context, It.IsAny<LdValue>(),
                5.0), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:tokens:output", context, It.IsAny<LdValue>(),
                5.0), Times.Once);
        }

        [Fact]
        public async Task TrackMetricsOf_ErrorPath_TracksErrorAndRethrows()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var mockLogger = new Mock<ILogger>();
            mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);
            var context = Context.New("key");
            var config = BuildConfig(mockClient.Object, "flag", context);
            var tracker = config.CreateTracker();

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await tracker.TrackMetricsOf<string>(
                    _ => new AiMetrics(true),
                    () => Task.FromException<string>(new InvalidOperationException("boom"))));

            mockClient.Verify(x => x.Track("$ld:ai:generation:error", context, It.IsAny<LdValue>(),
                1.0), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:duration:total", context, It.IsAny<LdValue>(),
                It.IsAny<double>()), Times.Once);
        }

        // ── TrackJudgeResult ────────────────────────────────────────────────

        [Fact]
        public void TrackJudgeResult_SampledFalse_NoEventEmitted()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var mockLogger = new Mock<ILogger>();
            mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);
            var context = Context.New("key");
            var config = BuildConfig(mockClient.Object, "flag", context);
            var tracker = config.CreateTracker();

            tracker.TrackJudgeResult(new JudgeResult(
                metricKey: "$ld:ai:judge:relevance",
                score: 0.9,
                sampled: false));

            mockClient.Verify(x => x.Track(It.IsAny<string>(), It.IsAny<Context>(),
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Never);
        }

        [Fact]
        public void TrackJudgeResult_SuccessFalse_NoEventEmitted()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var mockLogger = new Mock<ILogger>();
            mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);
            var context = Context.New("key");
            var config = BuildConfig(mockClient.Object, "flag", context);
            var tracker = config.CreateTracker();

            tracker.TrackJudgeResult(new JudgeResult(
                metricKey: "$ld:ai:judge:relevance",
                score: 0.9,
                success: false));

            mockClient.Verify(x => x.Track(It.IsAny<string>(), It.IsAny<Context>(),
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Never);
        }

        [Fact]
        public void TrackJudgeResult_SuccessPath_EmitsCorrectEvent()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var mockLogger = new Mock<ILogger>();
            mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);
            var context = Context.New("key");
            var config = BuildConfig(mockClient.Object, "flag", context);
            var tracker = config.CreateTracker();

            tracker.TrackJudgeResult(new JudgeResult(
                metricKey: "$ld:ai:judge:relevance",
                score: 0.85,
                judgeConfigKey: "my-judge"));

            mockClient.Verify(x => x.Track(
                "$ld:ai:judge:relevance",
                context,
                It.Is<LdValue>(d =>
                    d.Get("judgeConfigKey").AsString == "my-judge" &&
                    d.Get("runId").Type == LdValueType.String),
                0.85), Times.Once);
        }

        // ── TrackToolCall ───────────────────────────────────────────────────

        [Fact]
        public void TrackToolCall_DataIncludesToolKey()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var mockLogger = new Mock<ILogger>();
            mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);
            var context = Context.New("key");
            var config = BuildConfig(mockClient.Object, "flag", context);
            var tracker = config.CreateTracker();

            tracker.TrackToolCall("search");

            mockClient.Verify(x => x.Track(
                "$ld:ai:tool_call",
                context,
                It.Is<LdValue>(d =>
                    d.Get("toolKey").AsString == "search" &&
                    d.Get("runId").Type == LdValueType.String),
                1.0), Times.Once);
        }

        [Fact]
        public void TrackToolCall_NoAtMostOnce_EmitsMultipleEvents()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var mockLogger = new Mock<ILogger>();
            mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);
            var context = Context.New("key");
            var config = BuildConfig(mockClient.Object, "flag", context);
            var tracker = config.CreateTracker();

            tracker.TrackToolCall("search");
            tracker.TrackToolCall("fetch");
            tracker.TrackToolCall("search");

            mockClient.Verify(x => x.Track(
                "$ld:ai:tool_call",
                context,
                It.IsAny<LdValue>(),
                1.0), Times.Exactly(3));
        }

        [Fact]
        public void DeprecatedShims_StillCallable()
        {
            // Verify the obsolete shims still compile and execute without errors.
            // The [Obsolete] attribute generates a compile-time warning, not an error,
            // so this test confirms the methods remain functional.
            var mockClient = new Mock<ILaunchDarklyClient>();
            var mockLogger = new Mock<ILogger>();
            mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);
            var context = Context.New("key");
            var config = BuildConfig(mockClient.Object, "flag", context);
            var tracker = config.CreateTracker();

#pragma warning disable CS0618
            var durationTask = tracker.TrackDurationOfTask(Task.FromResult(42));
            var requestTask = tracker.TrackRequest(Task.FromResult(new Response(new Usage(1, 0, 1), null)));
#pragma warning restore CS0618

            Task.WaitAll(durationTask, requestTask);

            mockClient.Verify(x => x.Track("$ld:ai:duration:total", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Once);
        }

        // ── AiMetrics new fields ────────────────────────────────────────────

        [Fact]
        public void AiMetrics_BackwardCompat_OldConstructorStillWorks()
        {
            var tokens = new Usage(10, 4, 6);
            var metrics = new AiMetrics(true, tokens);

            Assert.True(metrics.Success);
            Assert.Equal(tokens, metrics.Tokens);
            Assert.Null(metrics.ToolCalls);
            Assert.Null(metrics.DurationMs);
        }

        [Fact]
        public void AiMetrics_NewFields_PopulatedCorrectly()
        {
            var tokens = new Usage(10, 4, 6);
            var toolCalls = new[] { "search", "fetch" };
            var metrics = new AiMetrics(true, tokens, toolCalls: toolCalls, durationMs: 42.0);

            Assert.True(metrics.Success);
            Assert.Equal(tokens, metrics.Tokens);
            Assert.Equal(toolCalls, metrics.ToolCalls);
            Assert.Equal(42.0, metrics.DurationMs);
        }

        // ── TrackMetricsOf tool calls and duration override ─────────────────

        [Fact]
        public async Task TrackMetricsOf_AutoTracksToolCalls()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var mockLogger = new Mock<ILogger>();
            mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);
            var context = Context.New("key");
            var config = BuildConfig(mockClient.Object, "flag", context);
            var tracker = config.CreateTracker();

            await tracker.TrackMetricsOf(
                _ => new AiMetrics(true, toolCalls: new[] { "search", "fetch" }),
                () => Task.FromResult("response"));

            mockClient.Verify(x => x.Track(
                "$ld:ai:tool_call",
                context,
                It.Is<LdValue>(d => d.Get("toolKey").AsString == "search"),
                1.0), Times.Once);
            mockClient.Verify(x => x.Track(
                "$ld:ai:tool_call",
                context,
                It.Is<LdValue>(d => d.Get("toolKey").AsString == "fetch"),
                1.0), Times.Once);
        }

        [Fact]
        public async Task TrackMetricsOf_DurationMsOverride()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var mockLogger = new Mock<ILogger>();
            mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);
            var context = Context.New("key");
            var config = BuildConfig(mockClient.Object, "flag", context);
            var tracker = config.CreateTracker();

            await tracker.TrackMetricsOf(
                _ => new AiMetrics(true, durationMs: 999.0),
                () => Task.FromResult("response"));

            mockClient.Verify(x => x.Track(
                "$ld:ai:duration:total",
                context,
                It.IsAny<LdValue>(),
                999.0), Times.Once);
        }

        [Fact]
        public async Task TrackMetricsOf_ExtractorException_TracksDurationButNotError()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var mockLogger = new Mock<ILogger>();
            mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);
            var context = Context.New("key");
            var config = BuildConfig(mockClient.Object, "flag", context);
            var tracker = config.CreateTracker();

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await tracker.TrackMetricsOf<string>(
                    _ => throw new InvalidOperationException("extractor boom"),
                    () => Task.FromResult("response")));

            mockClient.Verify(x => x.Track("$ld:ai:duration:total", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:generation:error", context,
                It.IsAny<LdValue>(), It.IsAny<double>()), Times.Never);
        }

        [Fact]
        public async Task TrackMetricsOf_DurationMsNull_UsesStopwatch()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var mockLogger = new Mock<ILogger>();
            mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);
            var context = Context.New("key");
            var config = BuildConfig(mockClient.Object, "flag", context);
            var tracker = config.CreateTracker();

            await tracker.TrackMetricsOf(
                _ => new AiMetrics(true),
                () => Task.FromResult("response"));

            mockClient.Verify(x => x.Track(
                "$ld:ai:duration:total",
                context,
                It.IsAny<LdValue>(),
                It.Is<double>(ms => ms >= 0)), Times.Once);
        }

        // ── Summary ToolCalls and ResumptionToken ───────────────────────────

        [Fact]
        public void Summary_IncludesToolCalls()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            mockClient.Setup(x => x.GetLogger()).Returns((ILogger)null);
            var context = Context.New("key");
            var config = BuildConfig(mockClient.Object, "flag", context);
            var tracker = config.CreateTracker();

            tracker.TrackToolCall("search");
            tracker.TrackToolCall("fetch");

            var summary = tracker.Summary;
            Assert.NotNull(summary.ToolCalls);
            Assert.Equal(2, summary.ToolCalls.Count);
            Assert.Equal("search", summary.ToolCalls[0]);
            Assert.Equal("fetch", summary.ToolCalls[1]);
        }

        [Fact]
        public void Summary_ToolCallsNullWhenNoneTracked()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            mockClient.Setup(x => x.GetLogger()).Returns((ILogger)null);
            var context = Context.New("key");
            var config = BuildConfig(mockClient.Object, "flag", context);
            var tracker = config.CreateTracker();

            Assert.Null(tracker.Summary.ToolCalls);
        }

        [Fact]
        public void Summary_IncludesResumptionToken()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            mockClient.Setup(x => x.GetLogger()).Returns((ILogger)null);
            var context = Context.New("key");
            var config = BuildConfig(mockClient.Object, "flag", context);
            var tracker = config.CreateTracker();

            var summary = tracker.Summary;
            Assert.NotNull(summary.ResumptionToken);
            Assert.Equal(tracker.ResumptionToken, summary.ResumptionToken);
        }
    }
}
