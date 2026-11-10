using System;
using System.IO;
using System.Linq;
using System.Threading;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Integrations;
using LaunchDarkly.Sdk.Server.Internal.Model;
using Xunit;
using Xunit.Abstractions;
using YamlDotNet.Serialization;

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
        public void YamlOverrideFileIsReadWithTheRegisteredParser()
        {
            var path = _dir.PathOf("overrides.yaml");
            File.WriteAllText(path, "flagValues:\n  overridden-flag: \"override-value\"\n");
            var yaml = new DeserializerBuilder().WithAttemptingUnquotedStringTypeDeserialization().Build();

            var config = BasicConfig()
                .DataSystem(Components.DataSystem().Custom()
                    .Synchronizers(MockComponents.MockDataSourceThatNeverStarts())
                    .Overrides(FileOverrides.Source().FilePaths(path)
                        .Parser("yaml", s => yaml.Deserialize<object>(s))))
                .Build();
            using (var client = new LdClient(config))
            {
                var detail = client.StringVariationDetail("overridden-flag", context, "default");
                Assert.Equal("override-value", detail.Value);
                Assert.True(detail.Reason.OverrideAffected);
            }
        }

        [Fact]
        public void ClientSideAllFlagsStateKeepsADefinitionWithClientSideButNotAFlagValuesEntry()
        {
            // A flagValues entry is not available to client-side SDKs. A full definition that sets
            // clientSide is, so that is how an operator overrides a flag that client-side SDKs use.
            var path = _dir.PathOf("overrides.json");
            File.WriteAllText(path, @"{""flagValues"": {""value-flag"": true},
                ""flags"": {""full-flag"": {""on"": true, ""variations"": [true],
                ""fallthrough"": {""variation"": 0}, ""clientSide"": true}}}");

            var config = BasicConfig()
                .DataSystem(Components.DataSystem().Custom()
                    .Synchronizers(MockComponents.MockDataSourceThatNeverStarts())
                    .Overrides(FileOverrides.Source().FilePaths(path)))
                .Build();
            using (var client = new LdClient(config))
            {
                var all = client.AllFlagsState(context).ToValuesJsonMap();
                Assert.Equal(new[] { "full-flag", "value-flag" }, all.Keys.OrderBy(k => k));

                var clientSide = client.AllFlagsState(context, FlagsStateOption.ClientSideOnly);
                Assert.True(clientSide.Valid);
                Assert.Equal(new[] { "full-flag" }, clientSide.ToValuesJsonMap().Keys);
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
                Assert.Equal(EvaluationReasonKind.Fallthrough, detail.Reason.Kind);
            }
        }

        [Fact]
        public void EditThatAddsADeletedEntryKeepsTheOverridesInEffect()
        {
            var path = _dir.PathOf("overrides.json");
            File.WriteAllText(path, @"{""flagValues"": {""overridden-flag"": ""override-value""}}");
            var launchDarklyData = new DataSetBuilder()
                .Flags(new FeatureFlagBuilder("ld-flag").Version(1).OffWithValue(LdValue.Of("ld-value")).Build())
                .Build();

            var config = BasicConfig()
                .StartWaitTime(TimeSpan.FromSeconds(5))
                .DataSystem(Components.DataSystem().Custom()
                    .Synchronizers(MockComponents.MockDataSourceWithData(launchDarklyData))
                    .Overrides(FileOverrides.Source().FilePaths(path)))
                .Build();
            using (var client = new LdClient(config))
            {
                Assert.True(client.Initialized);
                Assert.Equal("override-value", client.StringVariation("overridden-flag", context, "default"));

                // The edit changes the override and marks the LaunchDarkly flag as deleted. The file
                // fails to load, so neither change takes effect.
                File.WriteAllText(path, @"{""flagValues"": {""overridden-flag"": ""changed""},
                    ""flags"": {""ld-flag"": {""deleted"": true}}}");
                AssertEventually(() => LogCapture.GetMessages().Any(m => m.Level == LogLevel.Error &&
                    m.Text.Contains(path) && m.Text.Contains("flag \"ld-flag\" is marked as deleted")),
                    "the failed load to be logged");

                var overridden = client.StringVariationDetail("overridden-flag", context, "default");
                Assert.Equal("override-value", overridden.Value);
                Assert.True(overridden.Reason.OverrideAffected);
                var notOverridden = client.StringVariationDetail("ld-flag", context, "default");
                Assert.Equal("ld-value", notOverridden.Value);
                Assert.False(notOverridden.Reason.OverrideAffected);

                // Removing the deleted entry lets the rest of the edit take effect.
                File.WriteAllText(path, @"{""flagValues"": {""overridden-flag"": ""changed""}}");
                AssertEventually(() => client.StringVariation("overridden-flag", context, "default") == "changed",
                    "the corrected file to take effect");
                Assert.Equal("ld-value", client.StringVariation("ld-flag", context, "default"));
            }
        }
    }
}
