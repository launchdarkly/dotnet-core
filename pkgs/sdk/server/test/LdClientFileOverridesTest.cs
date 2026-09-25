using System;
using System.IO;
using System.Threading;
using LaunchDarkly.Sdk.Server.Integrations;
using Xunit;
using Xunit.Abstractions;

namespace LaunchDarkly.Sdk.Server
{
    // Drives the file-based override source through a running client. An operator writes, edits, and
    // empties an override file. Evaluations follow without any client restart, even though the client
    // never obtains data from LaunchDarkly.
    public class LdClientFileOverridesTest : BaseTest, IDisposable
    {
        private static readonly TimeSpan ReloadTimeout = TimeSpan.FromSeconds(10);
        private static readonly Context context = Context.New("userkey");

        private readonly TempDirectory _dir = TempDirectory.Create();

        public LdClientFileOverridesTest(ITestOutputHelper testOutput) : base(testOutput) { }

        public void Dispose() => _dir.Dispose();

        private static void AssertEventually(Func<bool> condition, string description)
        {
            var deadline = DateTime.UtcNow + ReloadTimeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return;
                }
                Thread.Sleep(50);
            }
            Assert.True(condition(), "timed out waiting for " + description);
        }

        [Theory]
        [InlineData(FileOverrideTypes.ChangeDetection.Polling)]
        [InlineData(FileOverrideTypes.ChangeDetection.Watching)]
        public void FileOverridesEndToEnd(FileOverrideTypes.ChangeDetection changeDetection)
        {
            var path = _dir.PathOf("overrides.json");
            File.WriteAllText(path, "{}");

            var config = BasicConfig()
                .DataSystem(Components.DataSystem().Custom()
                    .Synchronizers(MockComponents.MockDataSourceThatNeverStarts())
                    .Overrides(FileOverrides.Source().FilePaths(path).ChangeDetection(changeDetection)))
                .Build();
            using (var client = new LdClient(config))
            {
                Assert.False(client.Initialized);

                // Not initialized and no override present: the default is served.
                var detail = client.BoolVariationDetail("overridden-flag", context, false);
                Assert.False(detail.Value);
                Assert.Equal(EvaluationErrorKind.ClientNotReady, detail.Reason.ErrorKind);

                // An operator adds an override. The running client picks it up.
                File.WriteAllText(path, @"{""flagValues"": {""overridden-flag"": true}}");
                AssertEventually(() => client.BoolVariation("overridden-flag", context, false), "the override to take effect");

                // The override changes value.
                File.WriteAllText(path, @"{""flagValues"": {""overridden-flag"": false}}");
                AssertEventually(() =>
                {
                    var d = client.BoolVariationDetail("overridden-flag", context, true);
                    return d.Reason.OverrideAffected && !d.Value;
                }, "the changed override to take effect");

                // The override is removed. The not-initialized short-circuit returns.
                File.WriteAllText(path, "{}");
                AssertEventually(() =>
                    client.BoolVariationDetail("overridden-flag", context, false).Reason.ErrorKind == EvaluationErrorKind.ClientNotReady,
                    "the removed override to stop taking effect");
            }
        }

        [Fact]
        public void OverridesPresentAtStartupTakeEffectFromTheFirstEvaluation()
        {
            var path = _dir.PathOf("overrides.json");
            File.WriteAllText(path, @"{""flagValues"": {""overridden-flag"": ""override-value""}}");

            var config = BasicConfig()
                .DataSystem(Components.DataSystem().Custom()
                    .Synchronizers(MockComponents.MockDataSourceThatNeverStarts())
                    .Overrides(FileOverrides.Source().FilePaths(path)))
                .Build();
            using (var client = new LdClient(config))
            {
                var detail = client.StringVariationDetail("overridden-flag", context, "default");
                Assert.Equal("override-value", detail.Value);
                Assert.True(detail.Reason.OverrideAffected);
                Assert.Equal(EvaluationReasonKind.Off, detail.Reason.Kind);
            }
        }
    }
}
