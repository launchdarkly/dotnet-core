using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LaunchDarkly.EventSource;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Internal.Events;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Internal.DataSystem;
using LaunchDarkly.Sdk.Server.Subsystems;
using LaunchDarkly.TestHelpers;
using Moq;
using Xunit;
using Xunit.Abstractions;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

// Unused events on mock for interface.
#pragma warning disable 67

namespace LaunchDarkly.Sdk.Server.Internal.FDv2DataSources
{
    public class FDv2StreamingDataSourceTest : BaseTest
    {
        private static readonly TimeSpan BriefReconnectDelay = TimeSpan.FromMilliseconds(10);

        private readonly CapturingDataSourceUpdatesWithHeaders
            _updateSink = new CapturingDataSourceUpdatesWithHeaders();

        private readonly MockEventSource _mockEventSource = new MockEventSource();
        private readonly Mock<IDiagnosticStore> _mockDiagnosticStore = new Mock<IDiagnosticStore>();

        public FDv2StreamingDataSourceTest(ITestOutputHelper testOutput) : base(testOutput)
        {
        }

        private FDv2StreamingDataSource MakeDataSource(
            Uri baseUri = null,
            FDv2StreamingDataSource.EventSourceCreator eventSourceCreator = null)
        {
            if (baseUri == null)
            {
                baseUri = new Uri("http://example.com");
            }

            var context = BasicContext.WithDiagnosticStore(_mockDiagnosticStore.Object);

            var esc = eventSourceCreator ?? ((uri, config) => _mockEventSource);

            return new FDv2StreamingDataSource(
                context,
                _updateSink,
                baseUri,
                BriefReconnectDelay,
                () => Selector.Empty,
                esc
            );
        }

        private static MessageReceivedEventArgs CreateMessageEvent(string eventType, string jsonData)
        {
            return new MessageReceivedEventArgs(new MessageEvent(eventType, jsonData, null));
        }

        private static string CreateServerIntentJson(string intentCode, string payloadId, int target)
        {
            return
                $@"{{""payloads"":[{{""id"":""{payloadId}"",""target"":{target},""intentCode"":""{intentCode}"",""reason"":""test reason""}}]}}";
        }

        private static string CreatePutObjectJson(string kind, string key, int version, string objectJson = "{}")
        {
            return $@"{{""version"":{version},""kind"":""{kind}"",""key"":""{key}"",""object"":{objectJson}}}";
        }

        private static string CreateDeleteObjectJson(string kind, string key, int version)
        {
            return $@"{{""version"":{version},""kind"":""{kind}"",""key"":""{key}""}}";
        }

        private static string CreatePayloadTransferredJson(string state, int version)
        {
            return $@"{{""state"":""{state}"",""version"":{version}}}";
        }

        private static string CreateErrorJson(string id, string reason)
        {
            return $@"{{""payloadId"":""{id}"",""reason"":""{reason}""}}";
        }

        private static string CreateGoodbyeJson(string reason)
        {
            return $@"{{""reason"":""{reason}""}}";
        }

        [Fact]
        public void DataSourceNotInitializedBeforeReceivingData()
        {
            using (var dataSource = MakeDataSource())
            {
                dataSource.Start();
                _mockEventSource.TriggerOpen();

                Assert.False(dataSource.Initialized);
            }
        }

        [Fact]
        public async Task ServerIntentNoneMarksDataSourceInitialized()
        {
            using (var dataSource = MakeDataSource())
            {
                var startTask = dataSource.Start();

                _mockEventSource.TriggerOpen();
                _mockEventSource.TriggerMessage(CreateMessageEvent("server-intent",
                    CreateServerIntentJson("none", "test-payload", 1)));

                var result = await startTask;
                Assert.True(result);
                Assert.True(dataSource.Initialized);
            }
        }

        [Fact]
        public async Task FullTransferWithMultipleFlagsAndSegmentsInitializesStore()
        {
            using (var dataSource = MakeDataSource())
            {
                var startTask = dataSource.Start();

                _mockEventSource.TriggerOpen();
                _mockEventSource.TriggerMessage(CreateMessageEvent("server-intent",
                    CreateServerIntentJson("xfer-full", "p1", 355)));

                const string flag1Json = @"{""key"":""should-crash"",""on"":true,""version"":81}";
                _mockEventSource.TriggerMessage(CreateMessageEvent("put-object",
                    CreatePutObjectJson("flag", "should-crash", 81, flag1Json)));

                const string flag2Json = @"{""key"":""verbose-response"",""on"":true,""version"":334}";
                _mockEventSource.TriggerMessage(CreateMessageEvent("put-object",
                    CreatePutObjectJson("flag", "verbose-response", 334, flag2Json)));

                const string segmentJson = @"{""key"":""always-gift-card"",""version"":334}";
                _mockEventSource.TriggerMessage(CreateMessageEvent("put-object",
                    CreatePutObjectJson("segment", "always-gift-card", 334, segmentJson)));

                _mockEventSource.TriggerMessage(CreateMessageEvent("payload-transferred",
                    CreatePayloadTransferredJson("(p:p1:355)", 355)));

                var result = await startTask;
                Assert.True(result);

                var changeSet = _updateSink.Applies.ExpectValue();
                Assert.Equal(ChangeSetType.Full, changeSet.Type);
                var allData = changeSet.Data.SelectMany(kv => kv.Value.Items).ToList();

                Assert.Equal(3, allData.Count);
                Assert.Contains(allData, item => item.Key == "should-crash");
                Assert.Contains(allData, item => item.Key == "verbose-response");
                Assert.Contains(allData, item => item.Key == "always-gift-card");
            }
        }

        [Fact]
        public async Task FullTransferPassesEnvironmentIdFromHeaders()
        {
            using (var dataSource = MakeDataSource())
            {
                var headers = new List<KeyValuePair<string, IEnumerable<string>>>
                {
                    new KeyValuePair<string, IEnumerable<string>>("ld-region", new[] { "us-east-1" }),
                    new KeyValuePair<string, IEnumerable<string>>("x-ld-envid", new[] { "64af23df214e7e135156a6d6" })
                };

                var startTask = dataSource.Start();

                _mockEventSource.TriggerOpen(headers);
                _mockEventSource.TriggerMessage(CreateMessageEvent("server-intent",
                    CreateServerIntentJson("xfer-full", "p1", 1)));
                _mockEventSource.TriggerMessage(CreateMessageEvent("payload-transferred",
                    CreatePayloadTransferredJson("(p:p1:1)", 1)));

                await startTask;

                var changeSet = _updateSink.Applies.ExpectValue();
                Assert.Equal(ChangeSetType.Full, changeSet.Type);
                Assert.Equal("64af23df214e7e135156a6d6", changeSet.EnvironmentId);
            }
        }

        [Fact]
        public async Task EmptyFullTransferInitializesWithNoData()
        {
            using (var dataSource = MakeDataSource())
            {
                var startTask = dataSource.Start();

                _mockEventSource.TriggerOpen();
                _mockEventSource.TriggerMessage(CreateMessageEvent("server-intent",
                    CreateServerIntentJson("xfer-full", "p1", 1)));
                _mockEventSource.TriggerMessage(CreateMessageEvent("payload-transferred",
                    CreatePayloadTransferredJson("(p:p1:1)", 1)));

                await startTask;

                var changeSet = _updateSink.Applies.ExpectValue();
                Assert.Equal(ChangeSetType.Full, changeSet.Type);
                var allData = changeSet.Data.SelectMany(kv => kv.Value.Items).ToList();
                Assert.Empty(allData);
            }
        }

        [Fact]
        public async Task IncrementalTransferWithPutAppliesPartialChangeSet()
        {
            using (var dataSource = MakeDataSource())
            {
                var startTask = dataSource.Start();

                _mockEventSource.TriggerOpen();
                _mockEventSource.TriggerMessage(CreateMessageEvent("server-intent",
                    CreateServerIntentJson("xfer-changes", "p1", 1)));

                const string flagJson = @"{""key"":""new-flag"",""on"":true,""version"":10}";
                _mockEventSource.TriggerMessage(CreateMessageEvent("put-object",
                    CreatePutObjectJson("flag", "new-flag", 10, flagJson)));

                _mockEventSource.TriggerMessage(CreateMessageEvent("payload-transferred",
                    CreatePayloadTransferredJson("(p:p1:1)", 1)));

                await startTask;

                var changeSet = _updateSink.Applies.ExpectValue();
                Assert.Equal(ChangeSetType.Partial, changeSet.Type);
                var flags = changeSet.Data.FirstOrDefault(kv => kv.Key == DataModel.Features).Value.Items;
                Assert.Single(flags);
                Assert.Contains(flags, kv => kv.Key == "new-flag" && kv.Value.Item != null && kv.Value.Version == 10);
            }
        }

        [Fact]
        public async Task IncrementalTransferWithDeleteAppliesPartialChangeSet()
        {
            using (var dataSource = MakeDataSource())
            {
                var startTask = dataSource.Start();

                _mockEventSource.TriggerOpen();
                _mockEventSource.TriggerMessage(CreateMessageEvent("server-intent",
                    CreateServerIntentJson("xfer-changes", "p1", 1)));

                _mockEventSource.TriggerMessage(CreateMessageEvent("delete-object",
                    CreateDeleteObjectJson("flag", "old-flag", 11)));

                _mockEventSource.TriggerMessage(CreateMessageEvent("payload-transferred",
                    CreatePayloadTransferredJson("(p:p1:1)", 1)));

                await startTask;

                var changeSet = _updateSink.Applies.ExpectValue();
                Assert.Equal(ChangeSetType.Partial, changeSet.Type);
                var flags = changeSet.Data.FirstOrDefault(kv => kv.Key == DataModel.Features).Value.Items;
                Assert.Single(flags);
                Assert.Contains(flags, kv => kv.Key == "old-flag" && kv.Value.Item == null && kv.Value.Version == 11);
            }
        }

        [Fact]
        public void IncrementalTransferHandlesMixedPutAndDeleteForFlagsAndSegments()
        {
            using (var dataSource = MakeDataSource())
            {
                dataSource.Start();

                _mockEventSource.TriggerOpen();
                _mockEventSource.TriggerMessage(CreateMessageEvent("server-intent",
                    CreateServerIntentJson("xfer-changes", "p1", 1)));

                const string flag1Json = @"{""key"":""flag1"",""on"":true,""version"":15}";
                _mockEventSource.TriggerMessage(CreateMessageEvent("put-object",
                    CreatePutObjectJson("flag", "flag1", 15, flag1Json)));

                _mockEventSource.TriggerMessage(CreateMessageEvent("delete-object",
                    CreateDeleteObjectJson("flag", "flag2", 16)));

                const string segmentJson = @"{""key"":""segment1"",""version"":7}";
                _mockEventSource.TriggerMessage(CreateMessageEvent("put-object",
                    CreatePutObjectJson("segment", "segment1", 7, segmentJson)));

                _mockEventSource.TriggerMessage(CreateMessageEvent("delete-object",
                    CreateDeleteObjectJson("segment", "segment2", 8)));

                _mockEventSource.TriggerMessage(CreateMessageEvent("payload-transferred",
                    CreatePayloadTransferredJson("(p:p1:1)", 1)));

                var changeSet = _updateSink.Applies.ExpectValue();
                Assert.Equal(ChangeSetType.Partial, changeSet.Type);

                var flags = changeSet.Data.FirstOrDefault(kv => kv.Key == DataModel.Features).Value.Items.ToList();
                Assert.Equal(2, flags.Count);
                Assert.Contains(flags, kv => kv.Key == "flag1" && kv.Value.Item != null);
                Assert.Contains(flags, kv => kv.Key == "flag2" && kv.Value.Item == null);

                var segments = changeSet.Data.FirstOrDefault(kv => kv.Key == DataModel.Segments).Value.Items.ToList();
                Assert.Equal(2, segments.Count);
                Assert.Contains(segments, kv => kv.Key == "segment1" && kv.Value.Item != null);
                Assert.Contains(segments, kv => kv.Key == "segment2" && kv.Value.Item == null);
            }
        }

        [Fact]
        public async Task VMultipleSequentialTransferCyclesAreProcessedCorrectly()
        {
            using (var dataSource = MakeDataSource())
            {
                var startTask = dataSource.Start();

                _mockEventSource.TriggerOpen();
                _mockEventSource.TriggerMessage(CreateMessageEvent("server-intent",
                    CreateServerIntentJson("xfer-full", "p1", 1)));
                _mockEventSource.TriggerMessage(CreateMessageEvent("payload-transferred",
                    CreatePayloadTransferredJson("(p:p1:1)", 1)));

                await startTask;
                var fullChangeSet = _updateSink.Applies.ExpectValue();
                Assert.Equal(ChangeSetType.Full, fullChangeSet.Type);

                const string flagJson = @"{""key"":""flag1"",""on"":true,""version"":10}";
                _mockEventSource.TriggerMessage(CreateMessageEvent("put-object",
                    CreatePutObjectJson("flag", "flag1", 10, flagJson)));
                _mockEventSource.TriggerMessage(CreateMessageEvent("payload-transferred",
                    CreatePayloadTransferredJson("(p:p1:2)", 2)));

                var partialChangeSet1 = _updateSink.Applies.ExpectValue();
                Assert.Equal(ChangeSetType.Partial, partialChangeSet1.Type);
                var flags1 = partialChangeSet1.Data.FirstOrDefault(kv => kv.Key == DataModel.Features).Value.Items;
                Assert.Contains(flags1, kv => kv.Key == "flag1" && kv.Value.Item != null);

                _mockEventSource.TriggerMessage(CreateMessageEvent("delete-object",
                    CreateDeleteObjectJson("flag", "flag1", 11)));
                _mockEventSource.TriggerMessage(CreateMessageEvent("payload-transferred",
                    CreatePayloadTransferredJson("(p:p1:3)", 3)));

                var partialChangeSet2 = _updateSink.Applies.ExpectValue();
                Assert.Equal(ChangeSetType.Partial, partialChangeSet2.Type);
                var flags2 = partialChangeSet2.Data.FirstOrDefault(kv => kv.Key == DataModel.Features).Value.Items;
                Assert.Contains(flags2, kv => kv.Key == "flag1" && kv.Value.Item == null);
            }
        }

        [Fact]
        public void ErrorEventDiscardsPartialDataAndLogsReason()
        {
            using (var dataSource = MakeDataSource())
            {
                dataSource.Start();

                _mockEventSource.TriggerOpen();
                _mockEventSource.TriggerMessage(CreateMessageEvent("server-intent",
                    CreateServerIntentJson("xfer-full", "p1", 1)));

                const string flagJson = @"{""key"":""flag1"",""on"":true,""version"":10}";
                _mockEventSource.TriggerMessage(CreateMessageEvent("put-object",
                    CreatePutObjectJson("flag", "flag1", 10, flagJson)));

                _mockEventSource.TriggerMessage(CreateMessageEvent("error",
                    CreateErrorJson("p1", "Internal server error occurred")));

                AssertLogMessage(true, LogLevel.Error, "Internal server error occurred");
                _updateSink.Applies.ExpectNoValue();
            }
        }

        [Fact]
        public void GoodbyeEventLogsInfoWithReason()
        {
            using (var dataSource = MakeDataSource())
            {
                dataSource.Start();

                _mockEventSource.TriggerOpen();
                _mockEventSource.TriggerMessage(CreateMessageEvent("goodbye",
                    CreateGoodbyeJson("Server maintenance scheduled")));

                AssertLogMessage(true, LogLevel.Info, "Server maintenance scheduled");
            }
        }

        [Fact]
        public void HeartbeatEventIsSilentlyIgnored()
        {
            using (var dataSource = MakeDataSource())
            {
                dataSource.Start();

                _mockEventSource.TriggerOpen();
                _mockEventSource.TriggerMessage(CreateMessageEvent("heartbeat", "{}"));

                _updateSink.Applies.ExpectNoValue();
                _updateSink.StatusUpdates.ExpectNoValue();
            }
        }

        [Fact]
        public void UnknownEventTypeLogsError()
        {
            using (var dataSource = MakeDataSource())
            {
                dataSource.Start();

                _mockEventSource.TriggerOpen();
                _mockEventSource.TriggerMessage(CreateMessageEvent("unknown-custom-event", "{}"));

                AssertLogMessageRegex(true, LogLevel.Debug, ".*unknown event.*");
            }
        }

        [Fact]
        public void MalformedJsonTriggersInvalidDataErrorAndStreamRestart()
        {
            using (var dataSource = MakeDataSource())
            {
                dataSource.Start();

                _mockEventSource.TriggerOpen();
                _mockEventSource.TriggerMessage(CreateMessageEvent("server-intent", "{invalid json, missing quotes"));

                Assert.Equal(1, _mockEventSource.RestartCallCount);

                var status = _updateSink.StatusUpdates.ExpectValue();
                Assert.Equal(DataSourceState.Interrupted, status.State);
                Assert.True(status.LastError.HasValue);
                Assert.Equal(DataSourceStatus.ErrorKind.InvalidData, status.LastError.Value.Kind);
                Assert.True(status.LastError.Value.Recoverable, "Recoverable should be true for invalid data errors");
            }
        }

        [Fact]
        public void StoreApplyFailureWithoutStatusMonitoringCausesStreamRestart()
        {
            _updateSink.MockDataStoreStatusProvider.StatusMonitoringEnabled = false;
            _updateSink.AppliesShouldFail = 1;

            using (var dataSource = MakeDataSource())
            {
                dataSource.Start();

                _mockEventSource.TriggerOpen();
                _mockEventSource.TriggerMessage(CreateMessageEvent("server-intent",
                    CreateServerIntentJson("xfer-full", "p1", 1)));
                _mockEventSource.TriggerMessage(CreateMessageEvent("payload-transferred",
                    CreatePayloadTransferredJson("(p:p1:1)", 1)));

                Assert.Equal(1, _mockEventSource.RestartCallCount);
                AssertLogMessageRegex(true, LogLevel.Warn, ".*Restarting stream.*");
            }
        }

        [Fact]
        public void StoreApplyFailureForPartialChangeSetCausesStreamRestart()
        {
            _updateSink.MockDataStoreStatusProvider.StatusMonitoringEnabled = false;
            _updateSink.AppliesShouldFail = 1;

            using (var dataSource = MakeDataSource())
            {
                dataSource.Start();

                _mockEventSource.TriggerOpen();
                _mockEventSource.TriggerMessage(CreateMessageEvent("server-intent",
                    CreateServerIntentJson("xfer-changes", "p1", 1)));

                const string flagJson = @"{""key"":""flag1"",""on"":true,""version"":10}";
                _mockEventSource.TriggerMessage(CreateMessageEvent("put-object",
                    CreatePutObjectJson("flag", "flag1", 10, flagJson)));
                _mockEventSource.TriggerMessage(CreateMessageEvent("payload-transferred",
                    CreatePayloadTransferredJson("(p:p1:1)", 1)));

                Assert.Equal(1, _mockEventSource.RestartCallCount);
            }
        }

        [Fact]
        public void StoreFailureWithStatusMonitoringUpdatesStatusWithoutRestart()
        {
            _updateSink.MockDataStoreStatusProvider.StatusMonitoringEnabled = true;
            _updateSink.AppliesShouldFail = 1;

            using (var dataSource = MakeDataSource())
            {
                dataSource.Start();

                _mockEventSource.TriggerOpen();
                _mockEventSource.TriggerMessage(CreateMessageEvent("server-intent",
                    CreateServerIntentJson("xfer-full", "p1", 1)));
                _mockEventSource.TriggerMessage(CreateMessageEvent("payload-transferred",
                    CreatePayloadTransferredJson("(p:p1:1)", 1)));

                Assert.Equal(0, _mockEventSource.RestartCallCount);

                var status = _updateSink.StatusUpdates.ExpectValue();
                Assert.Equal(DataSourceState.Interrupted, status.State);
                Assert.NotNull(status.LastError);
                Assert.Equal(DataSourceStatus.ErrorKind.StoreError, status.LastError.Value.Kind);
                Assert.True(status.LastError.Value.Recoverable, "Recoverable should be true for store errors");
            }
        }

        [Fact]
        public void StoreRecoveryWithRefreshNeededRestartsStream()
        {
            _updateSink.MockDataStoreStatusProvider.StatusMonitoringEnabled = true;

            using (var dataSource = MakeDataSource())
            {
                dataSource.Start();

                _mockEventSource.TriggerOpen();
                _mockEventSource.TriggerMessage(CreateMessageEvent("server-intent",
                    CreateServerIntentJson("none", "test-payload", 1)));

                _updateSink.MockDataStoreStatusProvider.FireStatusChanged(
                    new DataStoreStatus { Available = false, RefreshNeeded = false });
                _updateSink.MockDataStoreStatusProvider.FireStatusChanged(
                    new DataStoreStatus { Available = true, RefreshNeeded = true });

                Assert.Equal(1, _mockEventSource.RestartCallCount);
                AssertLogMessage(true, LogLevel.Warn, "Restarting stream to refresh data after data store outage");
            }
        }

        [Fact]
        public void StoreRecoveryWithoutRefreshNeededDoesNotRestart()
        {
            _updateSink.MockDataStoreStatusProvider.StatusMonitoringEnabled = true;

            using (var dataSource = MakeDataSource())
            {
                dataSource.Start();

                _mockEventSource.TriggerOpen();
                _mockEventSource.TriggerMessage(CreateMessageEvent("server-intent",
                    CreateServerIntentJson("none", "test-payload", 1)));

                _updateSink.MockDataStoreStatusProvider.FireStatusChanged(
                    new DataStoreStatus { Available = false, RefreshNeeded = false });
                _updateSink.MockDataStoreStatusProvider.FireStatusChanged(
                    new DataStoreStatus { Available = true, RefreshNeeded = false });

                Assert.Equal(0, _mockEventSource.RestartCallCount);
            }
        }

        [Fact]
        public void RecoverableHttpErrorUpdatesStatusAndLogsWarning()
        {
            using (var dataSource = MakeDataSource())
            {
                dataSource.Start();

                var exception = new EventSourceServiceUnsuccessfulResponseException(503);
                _mockEventSource.TriggerError(exception);

                var status = _updateSink.StatusUpdates.ExpectValue();
                Assert.Equal(DataSourceState.Interrupted, status.State);
                Assert.NotNull(status.LastError);
                Assert.Equal(DataSourceStatus.ErrorKind.ErrorResponse, status.LastError.Value.Kind);
                Assert.Equal(503, status.LastError.Value.StatusCode);
                Assert.True(status.LastError.Value.Recoverable, "Recoverable should be true for recoverable errors");

                AssertLogMessageRegex(true, LogLevel.Warn, ".*will retry.*");
            }
        }

        [Fact]
        public void RecoverableHttpErrorWithFallbackHeaderSetsFDv1Fallback()
        {
            using (var dataSource = MakeDataSource())
            {
                dataSource.Start();

                var headers = new List<KeyValuePair<string, IEnumerable<string>>>
                {
                    new KeyValuePair<string, IEnumerable<string>>("x-ld-fd-fallback", new[] { "true" })
                };
                var exception = new EventSourceServiceUnsuccessfulResponseException(503, headers);
                _mockEventSource.TriggerError(exception);

                var status = _updateSink.StatusUpdates.ExpectValue();
                Assert.Equal(DataSourceState.Interrupted, status.State);
                Assert.NotNull(status.LastError);
                Assert.Equal(DataSourceStatus.ErrorKind.ErrorResponse, status.LastError.Value.Kind);
                Assert.Equal(503, status.LastError.Value.StatusCode);
                Assert.True(status.LastError.Value.FDv1Fallback, "FDv1Fallback should be true when fallback header is present");
                Assert.True(status.LastError.Value.Recoverable, "Recoverable should be true for recoverable errors");

                AssertLogMessageRegex(true, LogLevel.Warn, ".*will retry.*");
            }
        }

        [Fact]
        public async Task UnrecoverableHttpErrorStopsInitializationAndShutsDown()
        {
            using (var dataSource = MakeDataSource())
            {
                var startTask = dataSource.Start();

                var exception = new EventSourceServiceUnsuccessfulResponseException(403);
                _mockEventSource.TriggerError(exception);

                var status = _updateSink.StatusUpdates.ExpectValue();
                Assert.Equal(DataSourceState.Off, status.State);
                Assert.NotNull(status.LastError);
                Assert.Equal(DataSourceStatus.ErrorKind.ErrorResponse, status.LastError.Value.Kind);
                Assert.Equal(403, status.LastError.Value.StatusCode);
                Assert.False(status.LastError.Value.Recoverable, "Recoverable should be false for unrecoverable errors");

                var result = await startTask;
                Assert.False(result);
                Assert.False(dataSource.Initialized);

                AssertLogMessageRegex(true, LogLevel.Error, ".*403.*");
            }
        }

        [Fact]
        public async Task UnrecoverableHttpErrorWithFallbackHeaderSetsFDv1Fallback()
        {
            using (var dataSource = MakeDataSource())
            {
                var startTask = dataSource.Start();

                var headers = new List<KeyValuePair<string, IEnumerable<string>>>
                {
                    new KeyValuePair<string, IEnumerable<string>>("x-ld-fd-fallback", new[] { "true" })
                };
                var exception = new EventSourceServiceUnsuccessfulResponseException(401, headers);
                _mockEventSource.TriggerError(exception);

                var status = _updateSink.StatusUpdates.ExpectValue();
                Assert.Equal(DataSourceState.Off, status.State);
                Assert.NotNull(status.LastError);
                Assert.Equal(DataSourceStatus.ErrorKind.ErrorResponse, status.LastError.Value.Kind);
                Assert.Equal(401, status.LastError.Value.StatusCode);
                Assert.True(status.LastError.Value.FDv1Fallback, "FDv1Fallback should be true when fallback header is present");
                Assert.False(status.LastError.Value.Recoverable, "Recoverable should be false for unrecoverable errors");

                var result = await startTask;
                Assert.False(result);
                Assert.False(dataSource.Initialized);

                AssertLogMessageRegex(true, LogLevel.Error, ".*401.*");
            }
        }

        [Fact]
        public void RecoverableHttpErrorWithoutFallbackHeaderDoesNotSetFDv1Fallback()
        {
            using (var dataSource = MakeDataSource())
            {
                dataSource.Start();

                var headers = new List<KeyValuePair<string, IEnumerable<string>>>();
                var exception = new EventSourceServiceUnsuccessfulResponseException(503, headers);
                _mockEventSource.TriggerError(exception);

                var status = _updateSink.StatusUpdates.ExpectValue();
                Assert.Equal(DataSourceState.Interrupted, status.State);
                Assert.NotNull(status.LastError);
                Assert.Equal(DataSourceStatus.ErrorKind.ErrorResponse, status.LastError.Value.Kind);
                Assert.Equal(503, status.LastError.Value.StatusCode);
                Assert.False(status.LastError.Value.FDv1Fallback, "FDv1Fallback should be false when fallback header is not present");
                Assert.True(status.LastError.Value.Recoverable, "Recoverable should be true for recoverable errors");

                AssertLogMessageRegex(true, LogLevel.Warn, ".*will retry.*");
            }
        }

        [Fact]
        public void RecoverableHttpErrorWithFallbackHeaderFalseDoesNotSetFDv1Fallback()
        {
            using (var dataSource = MakeDataSource())
            {
                dataSource.Start();

                var headers = new List<KeyValuePair<string, IEnumerable<string>>>
                {
                    new KeyValuePair<string, IEnumerable<string>>("x-ld-fd-fallback", new[] { "false" })
                };
                var exception = new EventSourceServiceUnsuccessfulResponseException(503, headers);
                _mockEventSource.TriggerError(exception);

                var status = _updateSink.StatusUpdates.ExpectValue();
                Assert.Equal(DataSourceState.Interrupted, status.State);
                Assert.NotNull(status.LastError);
                Assert.Equal(DataSourceStatus.ErrorKind.ErrorResponse, status.LastError.Value.Kind);
                Assert.Equal(503, status.LastError.Value.StatusCode);
                Assert.False(status.LastError.Value.FDv1Fallback, "FDv1Fallback should be false when fallback header value is false");
                Assert.True(status.LastError.Value.Recoverable, "Recoverable should be true for recoverable errors");

                AssertLogMessageRegex(true, LogLevel.Warn, ".*will retry.*");
            }
        }

        [Fact]
        public async Task UnrecoverableHttpErrorWithFallbackHeaderFalseDoesNotSetFDv1Fallback()
        {
            using (var dataSource = MakeDataSource())
            {
                var startTask = dataSource.Start();

                var headers = new List<KeyValuePair<string, IEnumerable<string>>>
                {
                    new KeyValuePair<string, IEnumerable<string>>("x-ld-fd-fallback", new[] { "false" })
                };
                var exception = new EventSourceServiceUnsuccessfulResponseException(401, headers);
                _mockEventSource.TriggerError(exception);

                var status = _updateSink.StatusUpdates.ExpectValue();
                Assert.Equal(DataSourceState.Off, status.State);
                Assert.NotNull(status.LastError);
                Assert.Equal(DataSourceStatus.ErrorKind.ErrorResponse, status.LastError.Value.Kind);
                Assert.Equal(401, status.LastError.Value.StatusCode);
                Assert.False(status.LastError.Value.FDv1Fallback, "FDv1Fallback should be false when fallback header value is false");
                Assert.False(status.LastError.Value.Recoverable, "Recoverable should be false for unrecoverable errors");

                var result = await startTask;
                Assert.False(result);
                Assert.False(dataSource.Initialized);

                AssertLogMessageRegex(true, LogLevel.Error, ".*401.*");
            }
        }

        [Fact]
        public async Task UnrecoverableHttpError403ReportsOffStatusWithRecoverableFalse()
        {
            using (var dataSource = MakeDataSource())
            {
                var startTask = dataSource.Start();

                var exception = new EventSourceServiceUnsuccessfulResponseException(403);
                _mockEventSource.TriggerError(exception);

                var status = _updateSink.StatusUpdates.ExpectValue();
                Assert.Equal(DataSourceState.Off, status.State);
                Assert.NotNull(status.LastError);
                Assert.Equal(DataSourceStatus.ErrorKind.ErrorResponse, status.LastError.Value.Kind);
                Assert.Equal(403, status.LastError.Value.StatusCode);
                Assert.False(status.LastError.Value.Recoverable, "Recoverable should be false for unrecoverable errors");

                var result = await startTask;
                Assert.False(result);
                Assert.False(dataSource.Initialized);
            }
        }

        [Fact]
        public async Task UnrecoverableHttpError401ReportsOffStatusWithRecoverableFalse()
        {
            using (var dataSource = MakeDataSource())
            {
                var startTask = dataSource.Start();

                var exception = new EventSourceServiceUnsuccessfulResponseException(401);
                _mockEventSource.TriggerError(exception);

                var status = _updateSink.StatusUpdates.ExpectValue();
                Assert.Equal(DataSourceState.Off, status.State);
                Assert.NotNull(status.LastError);
                Assert.Equal(DataSourceStatus.ErrorKind.ErrorResponse, status.LastError.Value.Kind);
                Assert.Equal(401, status.LastError.Value.StatusCode);
                Assert.False(status.LastError.Value.Recoverable, "Recoverable should be false for unrecoverable errors");

                var result = await startTask;
                Assert.False(result);
                Assert.False(dataSource.Initialized);
            }
        }

        [Fact]
        public async Task UnrecoverableHttpErrorAfterInitializationReportsOffStatusWithRecoverableFalse()
        {
            using (var dataSource = MakeDataSource())
            {
                var startTask = dataSource.Start();

                _mockEventSource.TriggerOpen();
                _mockEventSource.TriggerMessage(CreateMessageEvent("server-intent",
                    CreateServerIntentJson("none", "test-payload", 1)));

                await startTask;
                Assert.True(dataSource.Initialized);

                // Now trigger an unrecoverable error after initialization
                var exception = new EventSourceServiceUnsuccessfulResponseException(403);
                _mockEventSource.TriggerError(exception);

                var status = _updateSink.StatusUpdates.ExpectValue();
                Assert.Equal(DataSourceState.Off, status.State);
                Assert.NotNull(status.LastError);
                Assert.Equal(DataSourceStatus.ErrorKind.ErrorResponse, status.LastError.Value.Kind);
                Assert.Equal(403, status.LastError.Value.StatusCode);
                Assert.False(status.LastError.Value.Recoverable, "Recoverable should be false for unrecoverable errors");
            }
        }

        [Fact]
        public void NetworkErrorUpdatesStatusToInterrupted()
        {
            using (var dataSource = MakeDataSource())
            {
                dataSource.Start();

                var exception = new System.IO.IOException("Network unreachable");
                _mockEventSource.TriggerError(exception);

                var status = _updateSink.StatusUpdates.ExpectValue();
                Assert.Equal(DataSourceState.Interrupted, status.State);
                Assert.NotNull(status.LastError);
                Assert.Equal(DataSourceStatus.ErrorKind.NetworkError, status.LastError.Value.Kind);
                Assert.True(status.LastError.Value.Recoverable, "Recoverable should be true for network errors");

                AssertLogMessageRegex(true, LogLevel.Warn, ".*EventSource error.*");
            }
        }

        [Fact]
        public void OpenEventResetsProtocolHandlerDiscardingPartialData()
        {
            using (var dataSource = MakeDataSource())
            {
                dataSource.Start();

                _mockEventSource.TriggerOpen();
                _mockEventSource.TriggerMessage(CreateMessageEvent("server-intent",
                    CreateServerIntentJson("xfer-full", "p1", 1)));

                const string flagJson = @"{""key"":""flag1"",""on"":true,""version"":10}";
                _mockEventSource.TriggerMessage(CreateMessageEvent("put-object",
                    CreatePutObjectJson("flag", "flag1", 10, flagJson)));

                _mockEventSource.TriggerOpen();

                _mockEventSource.TriggerMessage(CreateMessageEvent("server-intent",
                    CreateServerIntentJson("xfer-full", "p2", 2)));
                _mockEventSource.TriggerMessage(CreateMessageEvent("payload-transferred",
                    CreatePayloadTransferredJson("(p:p2:2)", 2)));

                var changeSet = _updateSink.Applies.ExpectValue();
                Assert.Empty(changeSet.Data.SelectMany(kv => kv.Value.Items));
            }
        }

        [Fact]
        public void StreamInitDiagnosticRecordedSuccessOnOpen()
        {
            var receivedFailed = new EventSink<bool>();
            _mockDiagnosticStore
                .Setup(m => m.AddStreamInit(It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<bool>()))
                .Callback((DateTime timestamp, TimeSpan duration, bool failed) => receivedFailed.Enqueue(failed));

            using (var dataSource = MakeDataSource())
            {
                dataSource.Start();
                _mockEventSource.TriggerOpen();

                Assert.False(receivedFailed.ExpectValue());
            }
        }

        [Fact]
        public void StreamInitDiagnosticRecordedFailureOnHttpError()
        {
            var receivedFailed = new EventSink<bool>();
            _mockDiagnosticStore
                .Setup(m => m.AddStreamInit(It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<bool>()))
                .Callback((DateTime timestamp, TimeSpan duration, bool failed) => receivedFailed.Enqueue(failed));

            using (var dataSource = MakeDataSource())
            {
                dataSource.Start();

                var exception = new EventSourceServiceUnsuccessfulResponseException(500);
                _mockEventSource.TriggerError(exception);

                Assert.True(receivedFailed.ExpectValue());
            }
        }

        [Fact]
        public void DisposeShutsDownEventSource()
        {
            var dataSource = MakeDataSource();
            dataSource.Start();
            _mockEventSource.TriggerOpen();

            dataSource.Dispose();

            Assert.True(_mockEventSource.IsClosed);
            
            var status = _updateSink.StatusUpdates.ExpectValue();
            Assert.Equal(DataSourceState.Off, status.State);
        }

        [Fact]
        public void DisposeUnsubscribesFromDataStoreStatusEvents()
        {
            _updateSink.MockDataStoreStatusProvider.StatusMonitoringEnabled = true;

            var dataSource = MakeDataSource();
            dataSource.Start();
            _mockEventSource.TriggerOpen();

            dataSource.Dispose();

            var status = _updateSink.StatusUpdates.ExpectValue();
            Assert.Equal(DataSourceState.Off, status.State);

            _updateSink.MockDataStoreStatusProvider.FireStatusChanged(
                new DataStoreStatus { Available = true, RefreshNeeded = true });

            Assert.Equal(0, _mockEventSource.RestartCallCount);
        }

        [Fact]
        public void ConsecutiveStoreFailuresLogWarningOnlyOnFirstFailure()
        {
            _updateSink.MockDataStoreStatusProvider.StatusMonitoringEnabled = false;
            _updateSink.AppliesShouldFail = 2;

            using (var dataSource = MakeDataSource())
            {
                dataSource.Start();

                _mockEventSource.TriggerOpen();
                _mockEventSource.TriggerMessage(CreateMessageEvent("server-intent",
                    CreateServerIntentJson("xfer-changes", "p1", 1)));

                const string flagJson = @"{""key"":""flag1"",""on"":true,""version"":10}";
                _mockEventSource.TriggerMessage(CreateMessageEvent("put-object",
                    CreatePutObjectJson("flag", "flag1", 10, flagJson)));
                _mockEventSource.TriggerMessage(CreateMessageEvent("payload-transferred",
                    CreatePayloadTransferredJson("(p:p1:1)", 1)));

                _mockEventSource.TriggerOpen();

                _mockEventSource.TriggerMessage(CreateMessageEvent("server-intent",
                    CreateServerIntentJson("xfer-changes", "p1", 2)));
                _mockEventSource.TriggerMessage(CreateMessageEvent("put-object",
                    CreatePutObjectJson("flag", "flag2", 11, flagJson)));
                _mockEventSource.TriggerMessage(CreateMessageEvent("payload-transferred",
                    CreatePayloadTransferredJson("(p:p1:2)", 2)));

                var warningCount = LogCapture.GetMessages()
                    .Count(m => m.Level == LogLevel.Warn && m.Text.Contains("Restarting stream"));

                Assert.Equal(1, warningCount);
            }
        }

        [Fact]
        public void StoreSuccessAfterFailureClearsFailureFlagAndAllowsLogging()
        {
            _updateSink.MockDataStoreStatusProvider.StatusMonitoringEnabled = false;
            _updateSink.AppliesShouldFail = 1;

            using (var dataSource = MakeDataSource())
            {
                dataSource.Start();

                _mockEventSource.TriggerOpen();
                _mockEventSource.TriggerMessage(CreateMessageEvent("server-intent",
                    CreateServerIntentJson("xfer-changes", "p1", 1)));

                var flagJson = @"{""key"":""flag1"",""on"":true,""version"":10}";
                _mockEventSource.TriggerMessage(CreateMessageEvent("put-object",
                    CreatePutObjectJson("flag", "flag1", 10, flagJson)));
                _mockEventSource.TriggerMessage(CreateMessageEvent("payload-transferred",
                    CreatePayloadTransferredJson("(p:p1:1)", 1)));

                Assert.Equal(1, _mockEventSource.RestartCallCount);

                // The first Apply that failed
                var failedChangeSet = _updateSink.Applies.ExpectValue();
                Assert.Equal(ChangeSetType.Partial, failedChangeSet.Type);

                _mockEventSource.TriggerOpen();

                _mockEventSource.TriggerMessage(CreateMessageEvent("server-intent",
                    CreateServerIntentJson("xfer-changes", "p1", 2)));
                const string flag2Json = @"{""key"":""flag2"",""on"":false,""version"":11}";
                _mockEventSource.TriggerMessage(CreateMessageEvent("put-object",
                    CreatePutObjectJson("flag", "flag2", 11, flag2Json)));
                _mockEventSource.TriggerMessage(CreateMessageEvent("payload-transferred",
                    CreatePayloadTransferredJson("(p:p1:2)", 2)));

                var changeSet = _updateSink.Applies.ExpectValue();
                Assert.Equal(ChangeSetType.Partial, changeSet.Type);
                var flags = changeSet.Data.FirstOrDefault(kv => kv.Key == DataModel.Features).Value.Items;
                Assert.Contains(flags, kv => kv.Key == "flag2");
            }
        }

        [Fact]
        public void PartialTransferStopsOnFirstApplyFailure()
        {
            _updateSink.MockDataStoreStatusProvider.StatusMonitoringEnabled = false;
            _updateSink.AppliesShouldFail = 1;

            using (var dataSource = MakeDataSource())
            {
                dataSource.Start();

                _mockEventSource.TriggerOpen();
                _mockEventSource.TriggerMessage(CreateMessageEvent("server-intent",
                    CreateServerIntentJson("xfer-changes", "p1", 1)));

                const string flag1Json = @"{""key"":""flag1"",""on"":true,""version"":10}";
                _mockEventSource.TriggerMessage(CreateMessageEvent("put-object",
                    CreatePutObjectJson("flag", "flag1", 10, flag1Json)));

                const string flag2Json = @"{""key"":""flag2"",""on"":false,""version"":11}";
                _mockEventSource.TriggerMessage(CreateMessageEvent("put-object",
                    CreatePutObjectJson("flag", "flag2", 11, flag2Json)));

                _mockEventSource.TriggerMessage(CreateMessageEvent("payload-transferred",
                    CreatePayloadTransferredJson("(p:p1:1)", 1)));

                var status = _updateSink.StatusUpdates.ExpectValue();
                Assert.Equal(DataSourceState.Interrupted, status.State);
                Assert.NotNull(status.LastError);
                Assert.Equal(DataSourceStatus.ErrorKind.StoreError, status.LastError.Value.Kind);
                Assert.True(status.LastError.Value.Recoverable, "Recoverable should be true for store errors");
            }
        }

        private class MockEventSource : IEventSource
        {
            public event EventHandler<StateChangedEventArgs> Opened;
            public event EventHandler<StateChangedEventArgs> Closed;
            public event EventHandler<MessageReceivedEventArgs> MessageReceived;
            public event EventHandler<ExceptionEventArgs> Error;
            public event EventHandler<CommentReceivedEventArgs> CommentReceived;

            public int RestartCallCount { get; private set; }
            public bool IsClosed { get; private set; }
            public ReadyState ReadyState { get; private set; } = ReadyState.Closed;

            public Task StartAsync()
            {
                ReadyState = ReadyState.Open;
                return Task.CompletedTask;
            }

            public void Close()
            {
                IsClosed = true;
            }

            public void Restart(bool forceNewConnection = false)
            {
                RestartCallCount++;
            }

            public void TriggerOpen(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers = null)
            {
                Opened?.Invoke(this, new StateChangedEventArgs(ReadyState.Open, headers));
            }

            public void TriggerMessage(MessageReceivedEventArgs args)
            {
                MessageReceived?.Invoke(this, args);
            }

            public void TriggerError(Exception exception)
            {
                Error?.Invoke(this, new ExceptionEventArgs(exception));
            }
        }
    }
}
