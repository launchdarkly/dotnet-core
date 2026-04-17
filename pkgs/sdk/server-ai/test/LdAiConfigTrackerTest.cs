using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
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
                    It.IsInRange<double>(0, 500, Moq.Range.Inclusive)), Times.Once);
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

        [Fact]
        public void CreateTrackerReturnsTrackerWhenDisabled()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            var config = LdAiConfig.Disabled;

            var tracker = new LdAiConfigTracker(mockClient.Object, "key", config, context);
            var newTracker = tracker.CreateTracker();

            Assert.NotNull(newTracker);
            Assert.NotSame(tracker, newTracker);
            Assert.Equal(tracker.Config, newTracker.Config);
        }

        [Fact]
        public void CreateTrackerReturnsNewTrackerWhenEnabled()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            var config = LdAiConfig.New().Enable().Build();

            var tracker = new LdAiConfigTracker(mockClient.Object, "key", config, context);
            var newTracker = tracker.CreateTracker();

            Assert.NotNull(newTracker);
            Assert.NotSame(tracker, newTracker);
            Assert.Equal(tracker.Config, newTracker.Config);
        }

        [Fact]
        public void CreateTrackerReturnsTrackerWithFreshRunId()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.New().Enable().Build();

            var tracker1 = new LdAiConfigTracker(mockClient.Object, flagKey, config, context);
            var tracker2 = tracker1.CreateTracker();

            tracker1.TrackDuration(10.0f);
            tracker2.TrackDuration(20.0f);

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
        public void CreateTrackerReturnsTrackerWithIndependentTrackingState()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.New().Enable().Build();

            var tracker = new LdAiConfigTracker(mockClient.Object, flagKey, config, context);

            // Track duration on the original tracker (exhausts at-most-once)
            tracker.TrackDuration(1.0f);

            // Create a new tracker - it should have fresh tracking state
            var newTracker = tracker.CreateTracker();
            newTracker.TrackDuration(2.0f);

            // Both calls should have gone through
            mockClient.Verify(x => x.Track("$ld:ai:duration:total", context,
                It.IsAny<LdValue>(), 1.0f), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:duration:total", context,
                It.IsAny<LdValue>(), 2.0f), Times.Once);
        }

        [Fact]
        public void CreateTrackerCanBeCalledMultipleTimes()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.New().Enable().Build();

            var original = new LdAiConfigTracker(mockClient.Object, flagKey, config, context);

            var tracker1 = original.CreateTracker();
            var tracker2 = original.CreateTracker();
            var tracker3 = original.CreateTracker();

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
            var config = LdAiConfig.New()
                .Enable()
                .SetModelName("test-model")
                .SetModelProviderName("test-provider")
                .Build();

            var tracker = new LdAiConfigTracker(mockClient.Object, "my-config-key", config, context);
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

            Assert.Equal("my-config-key", doc.RootElement.GetProperty("configKey").GetString());
            Assert.Equal(config.Version, doc.RootElement.GetProperty("version").GetInt32());
            Assert.True(doc.RootElement.GetProperty("runId").GetString().Length > 0);

            // variationKey is empty for builder-created configs, so it should be omitted
            Assert.False(doc.RootElement.TryGetProperty("variationKey", out _));

            // modelName and providerName should NOT be in the token
            Assert.False(doc.RootElement.TryGetProperty("modelName", out _));
            Assert.False(doc.RootElement.TryGetProperty("providerName", out _));
        }

        [Fact]
        public void ResumptionTokenIsUrlSafeBase64()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            var config = LdAiConfig.New().Enable().Build();

            var tracker = new LdAiConfigTracker(mockClient.Object, "key", config, context);
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
            var config = LdAiConfig.New().Enable().Build();

            var tracker = new LdAiConfigTracker(mockClient.Object, "key", config, context);

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
            var tracker = client.CompletionConfig("key", context);
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
            var config = LdAiConfig.New().Enable().Build();

            var original = new LdAiConfigTracker(mockClient.Object, "my-key", config, context);
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
    }
}
