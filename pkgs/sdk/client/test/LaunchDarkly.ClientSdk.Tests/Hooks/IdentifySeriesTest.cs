using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Client.Hooks;
using LaunchDarkly.Sdk.Client.Integrations;
using LaunchDarkly.Sdk.Client.Interfaces;
using LaunchDarkly.Sdk.Client.Internal.Hooks.Series;
using LaunchDarkly.Sdk.Client.Internal.Hooks.Executor;
using LaunchDarkly.Sdk.Client.Plugins;
using LaunchDarkly.Sdk.Integrations.Plugins;
using Xunit;
using LogLevel = LaunchDarkly.Logging.LogLevel;

namespace LaunchDarkly.Sdk.Client.Hooks
{
    using SeriesData = ImmutableDictionary<string, object>;

    public class IdentifySeriesTest : BaseTest
    {
        private class SpyHook : Hook
        {
            private readonly List<string> _recorder;

            public SpyHook(string name, List<string> recorder) : base(name)
            {
                _recorder = recorder;
            }

            public override SeriesData BeforeIdentify(IdentifySeriesContext context, SeriesData data)
            {
                _recorder.Add(Metadata.Name + "_before");
                return data;
            }

            public override SeriesData AfterIdentify(IdentifySeriesContext context, SeriesData data,
                IdentifySeriesResult result)
            {
                _recorder.Add(Metadata.Name + "_after");
                return data;
            }
        }

        private class ThrowingHook : SpyHook
        {
            private readonly string _beforeError;
            private readonly string _afterError;

            public ThrowingHook(string name, List<string> recorder, string beforeError, string afterError)
                : base(name, recorder)
            {
                _beforeError = beforeError;
                _afterError = afterError;
            }

            public override SeriesData BeforeIdentify(IdentifySeriesContext context, SeriesData data)
            {
                if (_beforeError != null)
                {
                    throw new Exception(_beforeError);
                }
                return base.BeforeIdentify(context, data);
            }

            public override SeriesData AfterIdentify(IdentifySeriesContext context, SeriesData data,
                IdentifySeriesResult result)
            {
                if (_afterError != null)
                {
                    throw new Exception(_afterError);
                }
                return base.AfterIdentify(context, data, result);
            }
        }

        private class DataCapturingHook : Hook
        {
            public IdentifySeriesResult CapturedResult { get; private set; }
            public SeriesData CapturedAfterData { get; private set; }

            public DataCapturingHook(string name) : base(name) { }

            public override SeriesData BeforeIdentify(IdentifySeriesContext context, SeriesData data)
            {
                var builder = data.ToBuilder();
                builder["before"] = "was called";
                return builder.ToImmutable();
            }

            public override SeriesData AfterIdentify(IdentifySeriesContext context, SeriesData data,
                IdentifySeriesResult result)
            {
                CapturedResult = result;
                CapturedAfterData = data;
                return data;
            }
        }

        private static Context MakeContext(string key = "test") => Context.New(key);

        [Theory]
        [InlineData(new string[] { }, new string[] { })]
        [InlineData(new[] { "a" }, new[] { "a_before", "a_after" })]
        [InlineData(new[] { "a", "b", "c" },
            new[] { "a_before", "b_before", "c_before", "c_after", "b_after", "a_after" })]
        public async Task HooksAreExecutedInLifoOrder(string[] hookNames, string[] executions)
        {
            var got = new List<string>();

            var context = MakeContext();

            var executor = new Executor(testLogger, hookNames.Select(name => new SpyHook(name, got)));

            await executor.IdentifySeries(context, TimeSpan.Zero, () => Task.FromResult(true));

            Assert.Equal(executions, got);
        }

        [Fact]
        public async Task MultipleExceptionsThrownFromDifferentStagesShouldNotPreventOtherStagesFromRunning()
        {
            var got = new List<string>();

            var seriesContext = new IdentifySeriesContext(MakeContext(), TimeSpan.Zero);

            var hooks = new List<Hook>
            {
                new ThrowingHook("a", got, "error in before!", "error in after!"),
                new SpyHook("b", got),
                new ThrowingHook("c", got, null, "error in after!"),
                new SpyHook("d", got),
                new ThrowingHook("e", got, "error in before!", null),
                new SpyHook("f", got)
            };

            var before = new BeforeIdentify(testLogger, hooks, EvaluationStage.Order.Forward);
            var after = new AfterIdentify(testLogger, hooks, EvaluationStage.Order.Reverse);

            var beforeData = before.Execute(seriesContext, null).ToList();

            Assert.True(beforeData.Count == hooks.Count);
            Assert.True(beforeData.All(d => d.Equals(SeriesData.Empty)));

            var afterData = after.Execute(seriesContext,
                new IdentifySeriesResult(IdentifySeriesResult.IdentifySeriesStatus.Completed), beforeData).ToList();

            Assert.True(afterData.Count == hooks.Count);
            Assert.True(afterData.All(d => d.Equals(SeriesData.Empty)));

            var expected = new List<string>
            {
                "b_before", "c_before", "d_before", "f_before",
                "f_after", "e_after", "d_after", "b_after"
            };
            Assert.Equal(expected, got);
        }

        [Theory]
        [InlineData("test-context-1", "LaunchDarkly Test hook", "before failed", "after failed")]
        [InlineData("test-context-2", "test-hook", "before exception!", "after exception!")]
        public async Task StageFailureLogsExpectedMessages(string contextKey, string hookName, string beforeError,
            string afterError)
        {
            var hooks = new List<Hook>
            {
                new ThrowingHook(hookName, new List<string>(), beforeError, afterError)
            };

            var context = MakeContext(contextKey);

            var executor = new Executor(testLogger, hooks);

            await executor.IdentifySeries(context, TimeSpan.Zero, () => Task.FromResult(true));

            Assert.True(logCapture.GetMessages().Count == 2);

            logCapture.HasMessageWithText(LogLevel.Error,
                $"During identify of context \"{contextKey}\", stage \"BeforeIdentify\" of hook \"{hookName}\" reported error: {beforeError}");
            logCapture.HasMessageWithText(LogLevel.Error,
                $"During identify of context \"{contextKey}\", stage \"AfterIdentify\" of hook \"{hookName}\" reported error: {afterError}");
        }

        [Fact]
        public async Task IdentifyResultIsCapturedAsCompleted()
        {
            var hook = new DataCapturingHook("capturing-hook");
            var context = MakeContext();

            var executor = new Executor(testLogger, new List<Hook> { hook });

            await executor.IdentifySeries(context, TimeSpan.Zero, () => Task.FromResult(true));

            Assert.NotNull(hook.CapturedResult);
            Assert.Equal(IdentifySeriesResult.IdentifySeriesStatus.Completed, hook.CapturedResult.Status);
        }

        [Fact]
        public async Task IdentifyResultIsCapturedAsErrorOnException()
        {
            var hook = new DataCapturingHook("capturing-hook");
            var context = MakeContext();

            var executor = new Executor(testLogger, new List<Hook> { hook });

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await executor.IdentifySeries(context, TimeSpan.Zero,
                    () => Task.FromException<bool>(new InvalidOperationException("identify failed"))));

            Assert.NotNull(hook.CapturedResult);
            Assert.Equal(IdentifySeriesResult.IdentifySeriesStatus.Error, hook.CapturedResult.Status);
        }

        [Fact]
        public async Task BeforeHookPassesDataToAfterHook()
        {
            var hook = new DataCapturingHook("capturing-hook");
            var context = MakeContext();

            var executor = new Executor(testLogger, new List<Hook> { hook });

            await executor.IdentifySeries(context, TimeSpan.Zero, () => Task.FromResult(true));

            Assert.NotNull(hook.CapturedAfterData);
            Assert.Equal("was called", hook.CapturedAfterData["before"]);
        }

        [Fact]
        public async Task IdentifyOperationResultIsReturned()
        {
            var got = new List<string>();
            var hooks = new List<Hook> { new SpyHook("a", got) };

            var context = MakeContext();
            var executor = new Executor(testLogger, hooks);

            var result = await executor.IdentifySeries(context, TimeSpan.Zero, () => Task.FromResult(true));

            Assert.True(result);
        }

        [Fact]
        public async Task IdentifyHookIsActivatedAfterInitAsync()
        {
            var hook = new IdentifyTrackingHook("init-hook");
            var plugin = new HookProvidingPlugin("test-plugin", hook);
            var config = Configuration.Builder("mobile-key", ConfigurationBuilder.AutoEnvAttributes.Disabled)
                .BackgroundModeManager(new MockBackgroundModeManager())
                .ConnectivityStateManager(new MockConnectivityStateManager(true))
                .DataSource(new MockDataSource().AsSingletonFactory<Subsystems.IDataSource>())
                .Events(Components.NoEvents)
                .Logging(testLogging)
                .Persistence(
                    Components.Persistence().Storage(
                        new MockPersistentDataStore().AsSingletonFactory<Subsystems.IPersistentDataStore>()))
                .Plugins(new PluginConfigurationBuilder().Add(plugin))
                .Build();

            using (var client = await TestUtil.CreateClientAsync(config, MakeContext(), TimeSpan.FromSeconds(5)))
            {
                Assert.True(hook.BeforeIdentifyCalled, "BeforeIdentify should have been called after InitAsync");
                Assert.True(hook.AfterIdentifyCalled, "AfterIdentify should have been called after InitAsync");
            }
        }

        private class IdentifyTrackingHook : Hook
        {
            public bool BeforeIdentifyCalled { get; private set; }
            public bool AfterIdentifyCalled { get; private set; }

            public IdentifyTrackingHook(string name) : base(name) { }

            public override SeriesData BeforeIdentify(IdentifySeriesContext context, SeriesData data)
            {
                BeforeIdentifyCalled = true;
                return data;
            }

            public override SeriesData AfterIdentify(IdentifySeriesContext context, SeriesData data,
                IdentifySeriesResult result)
            {
                AfterIdentifyCalled = true;
                return data;
            }
        }

        private class HookProvidingPlugin : Plugin
        {
            private readonly Hook _hook;

            public HookProvidingPlugin(string name, Hook hook) : base(name)
            {
                _hook = hook;
            }

            public override void Register(ILdClient client, EnvironmentMetadata metadata) { }

            public override IList<Hook> GetHooks(EnvironmentMetadata metadata)
            {
                return new List<Hook> { _hook };
            }
        }
    }
}
