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
        public void CanTrackDuration()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.Disabled;
            var data = LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                { "variationKey", LdValue.Of(config.VariationKey) },
                { "version", LdValue.Of(config.Version) },
                { "configKey", LdValue.Of(flagKey) },
                { "modelName", LdValue.Of(config.Model.Name) },
                { "providerName", LdValue.Of(config.Provider.Name) }
            });
            var tracker = new LdAiConfigTracker(mockClient.Object, flagKey, config, context);

            tracker.TrackDuration(1.0f);
            mockClient.Verify(x => x.Track("$ld:ai:duration:total", context, data, 1.0f), Times.Once);
        }

        [Fact]
        public void CanTrackTimeToFirstToken()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.Disabled;
            var data = LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                { "variationKey", LdValue.Of(config.VariationKey) },
                { "version", LdValue.Of(config.Version) },
                { "configKey", LdValue.Of(flagKey) },
                { "modelName", LdValue.Of(config.Model.Name) },
                { "providerName", LdValue.Of(config.Provider.Name) }
            });
            var tracker = new LdAiConfigTracker(mockClient.Object, flagKey, config, context);

            tracker.TrackTimeToFirstToken(1.0f);
            mockClient.Verify(x => x.Track("$ld:ai:tokens:ttf", context, data, 1.0f), Times.Once);
        }

        [Fact]
        public void CanTrackSuccess()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.Disabled;
            var data = LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                { "variationKey", LdValue.Of(config.VariationKey) },
                { "version", LdValue.Of(config.Version) },
                { "configKey", LdValue.Of(flagKey) },
                { "modelName", LdValue.Of(config.Model.Name) },
                { "providerName", LdValue.Of(config.Provider.Name) }
            });

            var tracker = new LdAiConfigTracker(mockClient.Object, flagKey, config, context);
            tracker.TrackSuccess();
            mockClient.Verify(x => x.Track("$ld:ai:generation:success", context, data, 1.0f), Times.Once);
        }


        [Fact]
        public void CanTrackError()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.Disabled;
            var data = LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                { "variationKey", LdValue.Of(config.VariationKey) },
                { "version", LdValue.Of(config.Version) },
                { "configKey", LdValue.Of(flagKey) },
                { "modelName", LdValue.Of(config.Model.Name) },
                { "providerName", LdValue.Of(config.Provider.Name) }
            });

            var tracker = new LdAiConfigTracker(mockClient.Object, flagKey, config, context);
            tracker.TrackError();
            mockClient.Verify(x => x.Track("$ld:ai:generation:error", context, data, 1.0f), Times.Once);
        }


        [Fact]
        public async void CanTrackDurationOfTask()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.Disabled;
            var data = LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                { "variationKey", LdValue.Of(config.VariationKey) },
                { "version", LdValue.Of(config.Version) },
                { "configKey", LdValue.Of(flagKey) },
                { "modelName", LdValue.Of(config.Model.Name) },
                { "providerName", LdValue.Of(config.Provider.Name) }
            });

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
                x => x.Track("$ld:ai:duration:total", context, data,
                    It.IsInRange<double>(0, 500, Range.Inclusive)), Times.Once);
        }


        [Fact]
        public void CanTrackFeedback()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.Disabled;
            var data = LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                { "variationKey", LdValue.Of(config.VariationKey) },
                { "version", LdValue.Of(config.Version) },
                { "configKey", LdValue.Of(flagKey) },
                { "modelName", LdValue.Of(config.Model.Name) },
                { "providerName", LdValue.Of(config.Provider.Name) }
            });

            var tracker = new LdAiConfigTracker(mockClient.Object, flagKey, config, context);
            tracker.TrackFeedback(Feedback.Positive);
            tracker.TrackFeedback(Feedback.Negative);

            mockClient.Verify(x => x.Track("$ld:ai:feedback:user:positive", context, data, 1.0f), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:feedback:user:negative", context, data, 1.0f), Times.Once);
        }

        [Fact]
        public void CanTrackTokens()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.Disabled;
            var data = LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                { "variationKey", LdValue.Of(config.VariationKey) },
                { "version", LdValue.Of(config.Version) },
                { "configKey", LdValue.Of(flagKey) },
                { "modelName", LdValue.Of(config.Model.Name) },
                { "providerName", LdValue.Of(config.Provider.Name) }
            });

            var tracker = new LdAiConfigTracker(mockClient.Object, flagKey, config, context);

            var givenUsage = new Usage
            {
                Total = 1,
                Input = 2,
                Output = 3
            };

            tracker.TrackTokens(givenUsage);
            mockClient.Verify(x => x.Track("$ld:ai:tokens:total", context, data, 1.0f), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:tokens:input", context, data, 2.0f), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:tokens:output", context, data, 3.0f), Times.Once);
        }

        [Fact]
        public void CanTrackResponseWithSpecificLatency()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.Disabled;
            var data = LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                { "variationKey", LdValue.Of(config.VariationKey) },
                { "version", LdValue.Of(config.Version) },
                { "configKey", LdValue.Of(flagKey) },
                { "modelName", LdValue.Of(config.Model.Name) },
                { "providerName", LdValue.Of(config.Provider.Name) }
            });

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
            mockClient.Verify(x => x.Track("$ld:ai:generation:success", context, data, 1.0f), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:tokens:total", context, data, 1.0f), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:tokens:input", context, data, 2.0f), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:tokens:output", context, data, 3.0f), Times.Once);
            mockClient.Verify(x => x.Track("$ld:ai:duration:total", context, data, 500.0f), Times.Once);
        }

        [Fact]
        public void CanTrackResponseWithPartialData()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.Disabled;
            var data = LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                { "variationKey", LdValue.Of(config.VariationKey) },
                { "version", LdValue.Of(config.Version) },
                { "configKey", LdValue.Of(flagKey) },
                { "modelName", LdValue.Of(config.Model.Name) },
                { "providerName", LdValue.Of(config.Provider.Name) }
            });

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
            mockClient.Verify(x => x.Track("$ld:ai:tokens:total", context, data, 1.0f), Times.Once);

            // if latency isn't provided via Statistics, then it is automatically measured.
            mockClient.Verify(x => x.Track("$ld:ai:duration:total", context, data, It.IsAny<double>()), Times.Once);
        }

        [Fact]
        public async Task CanTrackExceptionFromResponse()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var context = Context.New("key");
            const string flagKey = "key";
            var config = LdAiConfig.Disabled;
            var data = LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                { "variationKey", LdValue.Of(config.VariationKey) },
                { "version", LdValue.Of(config.Version) },
                { "configKey", LdValue.Of(flagKey) },
                { "modelName", LdValue.Of(config.Model.Name) },
                { "providerName", LdValue.Of(config.Provider.Name) }
            });

            var tracker = new LdAiConfigTracker(mockClient.Object, flagKey, config, context);

            await Assert.ThrowsAsync<System.Exception>(() => tracker.TrackRequest(Task.FromException<Response>(new System.Exception("I am an exception"))));

            mockClient.Verify(x => x.Track("$ld:ai:generation:error", context, data, 1.0f), Times.Once);

            // if latency isn't provided via Statistics, then it is automatically measured.
            mockClient.Verify(x => x.Track("$ld:ai:duration:total", context, data, It.IsAny<double>()), Times.Once);
        }
    }
}
