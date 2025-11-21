using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LaunchDarkly.EventSource;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Internal.Events;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.TestHelpers;
using Moq;
using Xunit;
using Xunit.Abstractions;

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
                () => FDv2Selector.Empty,
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

                var initData = _updateSink.Inits.ExpectValue();
                var allData = initData.Item1.Data.SelectMany(kv => kv.Value.Items).ToList();

                Assert.Equal(3, allData.Count);
                Assert.Contains(allData, item => item.Key == "should-crash");
                Assert.Contains(allData, item => item.Key == "verbose-response");
                Assert.Contains(allData, item => item.Key == "always-gift-card");
            }
        }

        [Fact]
        public async Task FullTransferPassesResponseHeadersToDataStore()
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

                var initData = _updateSink.Inits.ExpectValue();
                Assert.NotNull(initData.Item2);
                Assert.Contains(initData.Item2, h => h.Key == "ld-region");
                Assert.Contains(initData.Item2, h => h.Key == "x-ld-envid");
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

                var initData = _updateSink.Inits.ExpectValue();
                var allData = initData.Item1.Data.SelectMany(kv => kv.Value.Items).ToList();
                Assert.Empty(allData);
            }
        }

        [Fact]
        public async Task IncrementalTransferWithPutUpsertsFlagToStore()
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

                var upsert = _updateSink.Upserts.ExpectValue();
                Assert.Equal(DataModel.Features, upsert.Kind);
                Assert.Equal("new-flag", upsert.Key);
                Assert.NotNull(upsert.Item.Item);
                Assert.Equal(10, upsert.Item.Version);
            }
        }

        [Fact]
        public async Task IncrementalTransferWithDeleteUpsertsDeletedFlag()
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

                var upsert = _updateSink.Upserts.ExpectValue();
                Assert.Equal(DataModel.Features, upsert.Kind);
                Assert.Equal("old-flag", upsert.Key);
                Assert.Null(upsert.Item.Item);
                Assert.Equal(11, upsert.Item.Version);
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

                var upserts = new List<CapturingDataSourceUpdatesWithHeaders.UpsertParams>
                {
                    _updateSink.Upserts.ExpectValue(),
                    _updateSink.Upserts.ExpectValue(),
                    _updateSink.Upserts.ExpectValue(),
                    _updateSink.Upserts.ExpectValue()
                };

                Assert.Contains(upserts, u => u.Key == "flag1" && u.Kind == DataModel.Features && u.Item.Item != null);
                Assert.Contains(upserts, u => u.Key == "flag2" && u.Kind == DataModel.Features && u.Item.Item == null);
                Assert.Contains(upserts,
                    u => u.Key == "segment1" && u.Kind == DataModel.Segments && u.Item.Item != null);
                Assert.Contains(upserts,
                    u => u.Key == "segment2" && u.Kind == DataModel.Segments && u.Item.Item == null);
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
                _updateSink.Inits.ExpectValue();

                const string flagJson = @"{""key"":""flag1"",""on"":true,""version"":10}";
                _mockEventSource.TriggerMessage(CreateMessageEvent("put-object",
                    CreatePutObjectJson("flag", "flag1", 10, flagJson)));
                _mockEventSource.TriggerMessage(CreateMessageEvent("payload-transferred",
                    CreatePayloadTransferredJson("(p:p1:2)", 2)));

                var upsert1 = _updateSink.Upserts.ExpectValue();
                Assert.Equal("flag1", upsert1.Key);
                Assert.NotNull(upsert1.Item.Item);

                _mockEventSource.TriggerMessage(CreateMessageEvent("delete-object",
                    CreateDeleteObjectJson("flag", "flag1", 11)));
                _mockEventSource.TriggerMessage(CreateMessageEvent("payload-transferred",
                    CreatePayloadTransferredJson("(p:p1:3)", 3)));

                var upsert2 = _updateSink.Upserts.ExpectValue();
                Assert.Equal("flag1", upsert2.Key);
                Assert.Null(upsert2.Item.Item);
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
                _updateSink.Inits.ExpectNoValue();
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

                _updateSink.Inits.ExpectNoValue();
                _updateSink.Upserts.ExpectNoValue();
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

                AssertLogMessageRegex(true, LogLevel.Error, ".*unknown event.*");
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
            }
        }

        [Fact]
        public void StoreInitFailureWithoutStatusMonitoringCausesStreamRestart()
        {
            _updateSink.MockDataStoreStatusProvider.StatusMonitoringEnabled = false;
            _updateSink.InitsShouldFail = 1;

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
        public void StoreUpsertFailureWithoutStatusMonitoringCausesStreamRestart()
        {
            _updateSink.MockDataStoreStatusProvider.StatusMonitoringEnabled = false;
            _updateSink.UpsertsShouldFail = 1;

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
            _updateSink.InitsShouldFail = 1;

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

                var result = await startTask;
                Assert.False(result);
                Assert.False(dataSource.Initialized);

                AssertLogMessageRegex(true, LogLevel.Error, ".*403.*");
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

                var initData = _updateSink.Inits.ExpectValue();
                Assert.Empty(initData.Item1.Data.SelectMany(kv => kv.Value.Items));
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
        }

        [Fact]
        public void DisposeUnsubscribesFromDataStoreStatusEvents()
        {
            _updateSink.MockDataStoreStatusProvider.StatusMonitoringEnabled = true;

            var dataSource = MakeDataSource();
            dataSource.Start();
            _mockEventSource.TriggerOpen();

            dataSource.Dispose();

            _updateSink.MockDataStoreStatusProvider.FireStatusChanged(
                new DataStoreStatus { Available = true, RefreshNeeded = true });

            Assert.Equal(0, _mockEventSource.RestartCallCount);
        }

        [Fact]
        public void ConsecutiveStoreFailuresLogWarningOnlyOnFirstFailure()
        {
            _updateSink.MockDataStoreStatusProvider.StatusMonitoringEnabled = false;
            _updateSink.UpsertsShouldFail = 2;

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
            _updateSink.UpsertsShouldFail = 1;

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

                // Consume the failed upsert from the first cycle
                var failedUpsert = _updateSink.Upserts.ExpectValue();
                Assert.Equal("flag1", failedUpsert.Key);

                _mockEventSource.TriggerOpen();

                _mockEventSource.TriggerMessage(CreateMessageEvent("server-intent",
                    CreateServerIntentJson("xfer-changes", "p1", 2)));
                const string flag2Json = @"{""key"":""flag2"",""on"":false,""version"":11}";
                _mockEventSource.TriggerMessage(CreateMessageEvent("put-object",
                    CreatePutObjectJson("flag", "flag2", 11, flag2Json)));
                _mockEventSource.TriggerMessage(CreateMessageEvent("payload-transferred",
                    CreatePayloadTransferredJson("(p:p1:2)", 2)));

                var upsert = _updateSink.Upserts.ExpectValue();
                Assert.Equal("flag2", upsert.Key);
            }
        }

        [Fact]
        public void PartialTransferStopsOnFirstUpsertFailure()
        {
            _updateSink.MockDataStoreStatusProvider.StatusMonitoringEnabled = false;
            _updateSink.UpsertsShouldFail = 1;

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

                _updateSink.Upserts.ExpectValue();
                _updateSink.Upserts.ExpectNoValue();

                var status = _updateSink.StatusUpdates.ExpectValue();
                Assert.Equal(DataSourceState.Interrupted, status.State);
                Assert.NotNull(status.LastError);
                Assert.Equal(DataSourceStatus.ErrorKind.StoreError, status.LastError.Value.Kind);
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
