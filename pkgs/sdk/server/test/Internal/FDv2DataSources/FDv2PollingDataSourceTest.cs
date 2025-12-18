using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Internal.Http;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Internal.DataSystem;
using LaunchDarkly.Sdk.Server.Internal.FDv2Payloads;
using LaunchDarkly.Sdk.Server.Subsystems;
using LaunchDarkly.TestHelpers;
using Moq;
using Xunit;
using Xunit.Abstractions;
using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2DataSources
{
    public class FDv2PollingDataSourceTest : BaseTest
    {
        private static readonly TimeSpan BriefPollInterval = TimeSpan.FromMilliseconds(50);

        private readonly CapturingDataSourceUpdatesWithHeaders
            _updateSink = new CapturingDataSourceUpdatesWithHeaders();

        private readonly Mock<IFDv2PollingRequestor> _mockRequestor = new Mock<IFDv2PollingRequestor>();

        public FDv2PollingDataSourceTest(ITestOutputHelper testOutput) : base(testOutput)
        {
        }

        private FDv2PollingDataSource MakeDataSource(
            TimeSpan? pollInterval = null,
            FDv2PollingDataSource.SelectorSource selectorSource = null)
        {
            var defaultedSelectorSource = selectorSource ?? (() => Selector.Empty);

            return new FDv2PollingDataSource(
                BasicContext,
                _updateSink,
                _mockRequestor.Object,
                pollInterval ?? BriefPollInterval,
                defaultedSelectorSource
            );
        }

        private static FDv2PollingResponse CreatePollingResponse(
            params FDv2Event[] events)
        {
            return CreatePollingResponseWithHeaders(events, new List<KeyValuePair<string, IEnumerable<string>>>());
        }

        private static FDv2PollingResponse CreatePollingResponseWithHeaders(
            FDv2Event[] events,
            List<KeyValuePair<string, IEnumerable<string>>> headers)
        {
            return new FDv2PollingResponse(events, headers);
        }

        private static FDv2Event CreateServerIntentEvent(string intentCode, string payloadId, int target)
        {
            var json = $@"{{
                ""payloads"": [
                    {{
                        ""id"": ""{payloadId}"",
                        ""target"": {target},
                        ""intentCode"": ""{intentCode}"",
                        ""reason"": ""test reason""
                    }}
                ]
            }}";
            return new FDv2Event(FDv2EventTypes.ServerIntent, JsonDocument.Parse(json).RootElement);
        }

        private static FDv2Event CreatePutObjectEvent(string kind, string key, int version, string objectJson)
        {
            var json = $@"{{
                ""version"": {version},
                ""kind"": ""{kind}"",
                ""key"": ""{key}"",
                ""object"": {objectJson}
            }}";
            return new FDv2Event(FDv2EventTypes.PutObject, JsonDocument.Parse(json).RootElement);
        }

        private static FDv2Event CreateDeleteObjectEvent(string kind, string key, int version)
        {
            var json = $@"{{
                ""version"": {version},
                ""kind"": ""{kind}"",
                ""key"": ""{key}""
            }}";
            return new FDv2Event(FDv2EventTypes.DeleteObject, JsonDocument.Parse(json).RootElement);
        }

        private static FDv2Event CreatePayloadTransferredEvent(string state, int version)
        {
            var json = $@"{{
                ""state"": ""{state}"",
                ""version"": {version}
            }}";
            return new FDv2Event(FDv2EventTypes.PayloadTransferred, JsonDocument.Parse(json).RootElement);
        }

        private static FDv2Event CreateErrorEvent(string payloadId, string reason)
        {
            var json = $@"{{
                ""payloadId"": ""{payloadId}"",
                ""reason"": ""{reason}""
            }}";
            return new FDv2Event(FDv2EventTypes.Error, JsonDocument.Parse(json).RootElement);
        }

        private static FDv2Event CreateGoodbyeEvent(string reason)
        {
            var json = $@"{{
                ""reason"": ""{reason}""
            }}";
            return new FDv2Event(FDv2EventTypes.Goodbye, JsonDocument.Parse(json).RootElement);
        }

        [Fact]
        public void DataSourceNotInitializedBeforeReceivingData()
        {
            _mockRequestor.Setup(r => r.PollingRequestAsync(It.IsAny<Selector>()))
                .ReturnsAsync(CreatePollingResponse());

            using (var dataSource = MakeDataSource())
            {
                dataSource.Start();
                Assert.False(dataSource.Initialized);
            }
        }

        [Fact]
        public async Task ServerIntentNoneMarksDataSourceInitialized()
        {
            _mockRequestor.Setup(r => r.PollingRequestAsync(It.IsAny<Selector>()))
                .ReturnsAsync(CreatePollingResponse(
                    CreateServerIntentEvent("none", "test-payload", 1)
                ));

            using (var dataSource = MakeDataSource())
            {
                var startTask = dataSource.Start();

                var result = await startTask;
                Assert.True(result);
                Assert.True(dataSource.Initialized);

                var changeSet = _updateSink.Applies.ExpectValue();
                Assert.Equal(ChangeSetType.None, changeSet.Type);
            }
        }

        [Fact]
        public async Task FullTransferWithMultipleFlagsAndSegmentsInitializesDataSource()
        {
            const string flag1Json = @"{
                ""key"": ""should-crash"",
                ""on"": true,
                ""version"": 81,
                ""variations"": [""a"", ""b""],
                ""fallthrough"": {""variation"": 0},
                ""offVariation"": 1,
                ""salt"": ""salt1"",
                ""trackEvents"": false,
                ""trackEventsFallthrough"": false,
                ""deleted"": false,
                ""clientSide"": false
            }";
            const string flag2Json = @"{
                ""key"": ""verbose-response"",
                ""on"": true,
                ""version"": 334,
                ""variations"": [""x"", ""y""],
                ""fallthrough"": {""variation"": 0},
                ""offVariation"": 1,
                ""salt"": ""salt2"",
                ""trackEvents"": false,
                ""trackEventsFallthrough"": false,
                ""deleted"": false,
                ""clientSide"": false
            }";
            const string segmentJson = @"{
                ""key"": ""always-gift-card"",
                ""version"": 334,
                ""included"": [],
                ""excluded"": [],
                ""rules"": [],
                ""salt"": ""seg-salt"",
                ""deleted"": false
            }";

            _mockRequestor.Setup(r => r.PollingRequestAsync(It.IsAny<Selector>()))
                .ReturnsAsync(CreatePollingResponse(
                    CreateServerIntentEvent("xfer-full", "p1", 355),
                    CreatePutObjectEvent("flag", "should-crash", 81, flag1Json),
                    CreatePutObjectEvent("flag", "verbose-response", 334, flag2Json),
                    CreatePutObjectEvent("segment", "always-gift-card", 334, segmentJson),
                    CreatePayloadTransferredEvent("(p:p1:355)", 355)
                ));

            using (var dataSource = MakeDataSource())
            {
                var startTask = dataSource.Start();

                var result = await startTask;
                Assert.True(result);
                Assert.True(dataSource.Initialized);

                var changeSet = _updateSink.Applies.ExpectValue();
                Assert.Equal(ChangeSetType.Full, changeSet.Type);

                var allData = changeSet.Data.SelectMany(kv => kv.Value.Items).ToList();
                Assert.Equal(3, allData.Count);

                var flags = changeSet.Data.First(kv => kv.Key.Name == "features").Value.Items.ToList();
                Assert.Equal(2, flags.Count);
                Assert.Contains(flags, f => f.Key == "should-crash");
                Assert.Contains(flags, f => f.Key == "verbose-response");

                var segments = changeSet.Data.First(kv => kv.Key.Name == "segments").Value.Items.ToList();
                Assert.Single(segments);
                Assert.Contains(segments, s => s.Key == "always-gift-card");
            }
        }

        [Fact]
        public async Task PartialTransferWithFlagUpdateAppliesChanges()
        {
            const string flagJson = @"{
                ""key"": ""test-flag"",
                ""on"": false,
                ""version"": 100,
                ""variations"": [""off"", ""on""],
                ""fallthrough"": {""variation"": 0},
                ""offVariation"": 0,
                ""salt"": ""salt"",
                ""trackEvents"": false,
                ""trackEventsFallthrough"": false,
                ""deleted"": false,
                ""clientSide"": false
            }";

            _mockRequestor.Setup(r => r.PollingRequestAsync(It.IsAny<Selector>()))
                .ReturnsAsync(CreatePollingResponse(
                    CreateServerIntentEvent("xfer-changes", "p2", 200),
                    CreatePutObjectEvent("flag", "test-flag", 100, flagJson),
                    CreatePayloadTransferredEvent("(p:p2:200)", 200)
                ));

            using (var dataSource = MakeDataSource())
            {
                var startTask = dataSource.Start();

                var result = await startTask;
                Assert.True(result);
                Assert.True(dataSource.Initialized);

                var changeSet = _updateSink.Applies.ExpectValue();
                Assert.Equal(ChangeSetType.Partial, changeSet.Type);

                var flags = changeSet.Data.First(kv => kv.Key.Name == "features").Value.Items.ToList();
                Assert.Single(flags);
                Assert.Equal("test-flag", flags[0].Key);
                Assert.Equal(100, flags[0].Value.Version);
            }
        }

        [Fact]
        public async Task DeleteObjectAppliesCorrectly()
        {
            _mockRequestor.Setup(r => r.PollingRequestAsync(It.IsAny<Selector>()))
                .ReturnsAsync(CreatePollingResponse(
                    CreateServerIntentEvent("xfer-changes", "p3", 300),
                    CreateDeleteObjectEvent("flag", "old-flag", 99),
                    CreatePayloadTransferredEvent("(p:p3:300)", 300)
                ));

            using (var dataSource = MakeDataSource())
            {
                var startTask = dataSource.Start();

                var result = await startTask;
                Assert.True(result);
                Assert.True(dataSource.Initialized);

                var changeSet = _updateSink.Applies.ExpectValue();
                Assert.Equal(ChangeSetType.Partial, changeSet.Type);

                var flags = changeSet.Data.First(kv => kv.Key.Name == "features").Value.Items.ToList();
                Assert.Single(flags);
                Assert.Equal("old-flag", flags[0].Key);
                Assert.Null(flags[0].Value.Item); // Deleted items have null Item
                Assert.Equal(99, flags[0].Value.Version);
            }
        }

        [Fact]
        public async Task EnvironmentIdExtractedFromHeaders()
        {
            const string flagJson = @"{
                ""key"": ""test-flag"",
                ""on"": true,
                ""version"": 1,
                ""variations"": [""a""],
                ""fallthrough"": {""variation"": 0},
                ""salt"": ""salt"",
                ""trackEvents"": false,
                ""trackEventsFallthrough"": false,
                ""deleted"": false,
                ""clientSide"": false
            }";

            var headers = new List<KeyValuePair<string, IEnumerable<string>>>
            {
                new KeyValuePair<string, IEnumerable<string>>("x-ld-envid", new[] { "test-env-123" })
            };

            _mockRequestor.Setup(r => r.PollingRequestAsync(It.IsAny<Selector>()))
                .ReturnsAsync(CreatePollingResponseWithHeaders(
                    new[]
                    {
                        CreateServerIntentEvent("xfer-full", "p1", 1),
                        CreatePutObjectEvent("flag", "test-flag", 1, flagJson),
                        CreatePayloadTransferredEvent("(p:p1:1)", 1)
                    },
                    headers
                ));

            using (var dataSource = MakeDataSource())
            {
                await dataSource.Start();

                var changeSet = _updateSink.Applies.ExpectValue();
                Assert.Equal("test-env-123", changeSet.EnvironmentId);
            }
        }

        [Fact]
        public void HttpErrorRecoverableUpdatesStatusToInterrupted()
        {
            _mockRequestor.Setup(r => r.PollingRequestAsync(It.IsAny<Selector>()))
                .ThrowsAsync(new UnsuccessfulResponseException(503));

            using (var dataSource = MakeDataSource())
            {
                _ = dataSource.Start();

                var status = _updateSink.StatusUpdates.ExpectValue();
                Assert.Equal(DataSourceState.Interrupted, status.State);
                Assert.NotNull(status.LastError);
                Assert.Equal(DataSourceStatus.ErrorKind.ErrorResponse, status.LastError.Value.Kind);
                Assert.Equal(503, status.LastError.Value.StatusCode);

                Assert.False(dataSource.Initialized);
            }
        }

        [Fact]
        public async Task HttpErrorUnrecoverableUpdatesStatusToOffAndShutsDownDataSource()
        {
            _mockRequestor.Setup(r => r.PollingRequestAsync(It.IsAny<Selector>()))
                .ThrowsAsync(new UnsuccessfulResponseException(401));

            using (var dataSource = MakeDataSource())
            {
                var startTask = dataSource.Start();

                var result = await startTask;
                // The task completes with a false result.
                Assert.False(result);

                var status = _updateSink.StatusUpdates.ExpectValue();
                Assert.Equal(DataSourceState.Off, status.State);
                Assert.NotNull(status.LastError);
                Assert.Equal(DataSourceStatus.ErrorKind.ErrorResponse, status.LastError.Value.Kind);

                Assert.False(dataSource.Initialized);
            }
        }

        [Fact]
        public void SelectorPassedToRequestor()
        {
            var expectedSelector = Selector.Make(42, "test-state");
            var capturedSelectors = new EventSink<Selector>();

            _mockRequestor.Setup(r => r.PollingRequestAsync(It.IsAny<Selector>()))
                .Callback<Selector>(s => capturedSelectors.Enqueue(s))
                .ReturnsAsync(CreatePollingResponse());

            using (var dataSource = MakeDataSource(selectorSource: () => expectedSelector))
            {
                dataSource.Start();

                var actualSelector = capturedSelectors.ExpectValue();
                Assert.Equal(expectedSelector.Version, actualSelector.Version);
                Assert.Equal(expectedSelector.State, actualSelector.State);
            }
        }

        [Fact]
        public async Task ErrorEventLogged()
        {
            _mockRequestor.Setup(r => r.PollingRequestAsync(It.IsAny<Selector>()))
                .ReturnsAsync(CreatePollingResponse(
                    CreateErrorEvent("payload-123", "test error reason")
                ));

            using (var dataSource = MakeDataSource())
            {
                _ = dataSource.Start();

                // Wait for a poll to happen
                await Task.Delay(TimeSpan.FromMilliseconds(100));

                // Just verify it doesn't crash - error is logged
                Assert.False(dataSource.Initialized);
            }
        }

        [Fact]
        public async Task GoodbyeEventLogged()
        {
            _mockRequestor.Setup(r => r.PollingRequestAsync(It.IsAny<Selector>()))
                .ReturnsAsync(CreatePollingResponse(
                    CreateGoodbyeEvent("server shutting down")
                ));

            using (var dataSource = MakeDataSource())
            {
                _ = dataSource.Start();

                // Wait for a poll to happen
                await Task.Delay(TimeSpan.FromMilliseconds(100));

                // Just verify it doesn't crash - goodbye is logged
                Assert.False(dataSource.Initialized);
            }
        }

        [Fact]
        public async Task PollingContinuesAfterInitialization()
        {
            const string flag1Json = @"{
                ""key"": ""flag1"",
                ""on"": true,
                ""version"": 1,
                ""variations"": [""a""],
                ""fallthrough"": {""variation"": 0},
                ""salt"": ""salt1"",
                ""trackEvents"": false,
                ""trackEventsFallthrough"": false,
                ""deleted"": false,
                ""clientSide"": false
            }";
            const string flag2Json = @"{
                ""key"": ""flag2"",
                ""on"": false,
                ""version"": 2,
                ""variations"": [""b""],
                ""fallthrough"": {""variation"": 0},
                ""salt"": ""salt2"",
                ""trackEvents"": false,
                ""trackEventsFallthrough"": false,
                ""deleted"": false,
                ""clientSide"": false
            }";

            var callCount = 0;
            _mockRequestor.Setup(r => r.PollingRequestAsync(It.IsAny<Selector>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    if (callCount == 1)
                    {
                        // First poll: full transfer
                        return CreatePollingResponse(
                            CreateServerIntentEvent("xfer-full", "p1", 1),
                            CreatePutObjectEvent("flag", "flag1", 1, flag1Json),
                            CreatePayloadTransferredEvent("(p:p1:1)", 1)
                        );
                    }
                    else
                    {
                        // Subsequent polls: partial transfer
                        return CreatePollingResponse(
                            CreateServerIntentEvent("xfer-changes", "p2", 2),
                            CreatePutObjectEvent("flag", "flag2", 2, flag2Json),
                            CreatePayloadTransferredEvent("(p:p2:2)", 2)
                        );
                    }
                });

            using (var dataSource = MakeDataSource())
            {
                await dataSource.Start();
                Assert.True(dataSource.Initialized);

                // ExpectValue blocks until the second Apply arrives
                var changeSet1 = _updateSink.Applies.ExpectValue();
                Assert.Equal(ChangeSetType.Full, changeSet1.Type);

                var changeSet2 = _updateSink.Applies.ExpectValue();
                Assert.Equal(ChangeSetType.Partial, changeSet2.Type);
            }
        }

        [Fact]
        public async Task NotModifiedResponseSkipsProcessing()
        {
            var callCount = 0;
            _mockRequestor.Setup(r => r.PollingRequestAsync(It.IsAny<Selector>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    if (callCount == 1)
                    {
                        // First poll: return data
                        const string flagJson = @"{
                            ""key"": ""test-flag"",
                            ""on"": true,
                            ""version"": 1,
                            ""variations"": [""a""],
                            ""fallthrough"": {""variation"": 0},
                            ""salt"": ""salt"",
                            ""trackEvents"": false,
                            ""trackEventsFallthrough"": false,
                            ""deleted"": false,
                            ""clientSide"": false
                        }";

                        return CreatePollingResponse(
                            CreateServerIntentEvent("xfer-full", "p1", 1),
                            CreatePutObjectEvent("flag", "test-flag", 1, flagJson),
                            CreatePayloadTransferredEvent("(p:p1:1)", 1)
                        );
                    }
                    else
                    {
                        // Second poll: return null (304 Not Modified)
                        return null;
                    }
                });

            using (var dataSource = MakeDataSource())
            {
                await dataSource.Start();
                Assert.True(dataSource.Initialized);

                // First poll should apply data
                var changeSet1 = _updateSink.Applies.ExpectValue();
                Assert.Equal(ChangeSetType.Full, changeSet1.Type);

                // Second poll should return 304, so no second Apply
                _updateSink.Applies.ExpectNoValue(TimeSpan.FromMilliseconds(150));
            }
        }

        [Fact]
        public async Task NotModifiedBeforeInitializationStillWaitsForData()
        {
            var callCount = 0;
            _mockRequestor.Setup(r => r.PollingRequestAsync(It.IsAny<Selector>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    if (callCount <= 2)
                    {
                        // First two polls: return null (304 Not Modified)
                        return null;
                    }
                    else
                    {
                        // Third poll: return actual data
                        const string flagJson = @"{
                            ""key"": ""test-flag"",
                            ""on"": true,
                            ""version"": 1,
                            ""variations"": [""a""],
                            ""fallthrough"": {""variation"": 0},
                            ""salt"": ""salt"",
                            ""trackEvents"": false,
                            ""trackEventsFallthrough"": false,
                            ""deleted"": false,
                            ""clientSide"": false
                        }";

                        return CreatePollingResponse(
                            CreateServerIntentEvent("xfer-full", "p1", 1),
                            CreatePutObjectEvent("flag", "test-flag", 1, flagJson),
                            CreatePayloadTransferredEvent("(p:p1:1)", 1)
                        );
                    }
                });

            using (var dataSource = MakeDataSource())
            {
                var startTask = dataSource.Start();

                // Should not be initialized yet (received 304s)
                Assert.False(dataSource.Initialized);

                // Wait for actual data on third poll
                var result = await startTask;
                Assert.True(result);
                Assert.True(dataSource.Initialized);

                var changeSet = _updateSink.Applies.ExpectValue();
                Assert.Equal(ChangeSetType.Full, changeSet.Type);
            }
        }

        [Fact]
        public async Task NotModifiedResponseUpdatesStatusToValid()
        {
            var callCount = 0;
            _mockRequestor.Setup(r => r.PollingRequestAsync(It.IsAny<Selector>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    if (callCount == 1)
                    {
                        // First poll: return data to initialize
                        const string flagJson = @"{
                            ""key"": ""test-flag"",
                            ""on"": true,
                            ""version"": 1,
                            ""variations"": [""a""],
                            ""fallthrough"": {""variation"": 0},
                            ""salt"": ""salt"",
                            ""trackEvents"": false,
                            ""trackEventsFallthrough"": false,
                            ""deleted"": false,
                            ""clientSide"": false
                        }";

                        return CreatePollingResponse(
                            CreateServerIntentEvent("xfer-full", "p1", 1),
                            CreatePutObjectEvent("flag", "test-flag", 1, flagJson),
                            CreatePayloadTransferredEvent("(p:p1:1)", 1)
                        );
                    }
                    else
                    {
                        // Second poll: return null (304 Not Modified)
                        return null;
                    }
                });

            using (var dataSource = MakeDataSource())
            {
                await dataSource.Start();
                Assert.True(dataSource.Initialized);

                // First poll should apply data and update status
                var changeSet = _updateSink.Applies.ExpectValue();
                Assert.Equal(ChangeSetType.Full, changeSet.Type);

                // Wait for second poll to happen (304 response)
                // This should update status to Valid
                var status = _updateSink.StatusUpdates.ExpectValue();
                Assert.Equal(DataSourceState.Valid, status.State);
                Assert.Null(status.LastError);
            }
        }

        [Fact]
        public async Task NotModifiedResponseRecoverStatusFromInterruptedToValid()
        {
            var callCount = 0;
            _mockRequestor.Setup(r => r.PollingRequestAsync(It.IsAny<Selector>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    if (callCount == 1)
                    {
                        // First poll: return data to initialize
                        const string flagJson = @"{
                            ""key"": ""test-flag"",
                            ""on"": true,
                            ""version"": 1,
                            ""variations"": [""a""],
                            ""fallthrough"": {""variation"": 0},
                            ""salt"": ""salt"",
                            ""trackEvents"": false,
                            ""trackEventsFallthrough"": false,
                            ""deleted"": false,
                            ""clientSide"": false
                        }";

                        return CreatePollingResponse(
                            CreateServerIntentEvent("xfer-full", "p1", 1),
                            CreatePutObjectEvent("flag", "test-flag", 1, flagJson),
                            CreatePayloadTransferredEvent("(p:p1:1)", 1)
                        );
                    }
                    else if (callCount == 2)
                    {
                        // Second poll: throw recoverable error
                        throw new UnsuccessfulResponseException(503);
                    }
                    else
                    {
                        // Third poll: return null (304 Not Modified) - should recover to Valid
                        return null;
                    }
                });

            using (var dataSource = MakeDataSource())
            {
                await dataSource.Start();
                Assert.True(dataSource.Initialized);

                // First poll should apply data
                var changeSet = _updateSink.Applies.ExpectValue();
                Assert.Equal(ChangeSetType.Full, changeSet.Type);

                // Second poll should set status to Interrupted
                var interruptedStatus = _updateSink.StatusUpdates.ExpectValue();
                Assert.Equal(DataSourceState.Interrupted, interruptedStatus.State);
                Assert.NotNull(interruptedStatus.LastError);
                Assert.Equal(503, interruptedStatus.LastError.Value.StatusCode);

                // Third poll returns 304, should recover status to Valid
                var validStatus = _updateSink.StatusUpdates.ExpectValue();
                Assert.Equal(DataSourceState.Valid, validStatus.State);
                Assert.Null(validStatus.LastError);
            }
        }

        [Fact]
        public async Task JsonErrorInEventUpdatesStatusToInterrupted()
        {
            // Create an event with malformed JSON data that will trigger a JsonError
            var badDataEvent = new FDv2Event(FDv2EventTypes.ServerIntent,
                JsonDocument.Parse(@"{""invalid"":""data""}").RootElement);

            _mockRequestor.Setup(r => r.PollingRequestAsync(It.IsAny<Selector>()))
                .ReturnsAsync(CreatePollingResponse(badDataEvent));

            using (var dataSource = MakeDataSource())
            {
                _ = dataSource.Start();

                // Wait for poll to happen and process the malformed event
                await Task.Delay(TimeSpan.FromMilliseconds(100));

                // Should update status to Interrupted with InvalidData error kind
                var status = _updateSink.StatusUpdates.ExpectValue();
                Assert.Equal(DataSourceState.Interrupted, status.State);
                Assert.NotNull(status.LastError);
                Assert.Equal(DataSourceStatus.ErrorKind.InvalidData, status.LastError.Value.Kind);
                Assert.Contains("Failed to deserialize", status.LastError.Value.Message);

                // Data source should not be initialized due to the error
                Assert.False(dataSource.Initialized);
            }
        }

        [Fact]
        public async Task JsonErrorInEventHasInvalidDataErrorKind()
        {
            var callCount = 0;
            _mockRequestor.Setup(r => r.PollingRequestAsync(It.IsAny<Selector>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    if (callCount == 1)
                    {
                        // First poll: return valid data to initialize
                        const string flagJson = @"{
                            ""key"": ""test-flag"",
                            ""on"": true,
                            ""version"": 1,
                            ""variations"": [""a""],
                            ""fallthrough"": {""variation"": 0},
                            ""salt"": ""salt"",
                            ""trackEvents"": false,
                            ""trackEventsFallthrough"": false,
                            ""deleted"": false,
                            ""clientSide"": false
                        }";

                        return CreatePollingResponse(
                            CreateServerIntentEvent("xfer-full", "p1", 1),
                            CreatePutObjectEvent("flag", "test-flag", 1, flagJson),
                            CreatePayloadTransferredEvent("(p:p1:1)", 1)
                        );
                    }
                    else
                    {
                        // Second poll: return response with malformed event
                        var badEvent = new FDv2Event(FDv2EventTypes.PutObject,
                            JsonDocument.Parse(@"{""missing"":""required fields""}").RootElement);
                        return CreatePollingResponse(badEvent);
                    }
                });

            using (var dataSource = MakeDataSource())
            {
                await dataSource.Start();
                Assert.True(dataSource.Initialized);

                // First poll should initialize successfully
                var changeSet = _updateSink.Applies.ExpectValue();
                Assert.Equal(ChangeSetType.Full, changeSet.Type);

                // Second poll should set status to Interrupted with InvalidData error
                var status = _updateSink.StatusUpdates.ExpectValue();
                Assert.Equal(DataSourceState.Interrupted, status.State);
                Assert.NotNull(status.LastError);
                Assert.Equal(DataSourceStatus.ErrorKind.InvalidData, status.LastError.Value.Kind);
                Assert.Contains("Failed to deserialize", status.LastError.Value.Message);

                // Data source should remain initialized
                Assert.True(dataSource.Initialized);
            }
        }

        [Fact]
        public void RecoverableHttpErrorWithFallbackHeaderSetsFDv1Fallback()
        {
            // Create an HttpResponseMessage with the fallback header
            using (var response = new HttpResponseMessage((HttpStatusCode)503))
            {
                response.Headers.Add("x-ld-fd-fallback", "true");
                var exception = new UnsuccessfulResponseException(503, response.Headers);

                _mockRequestor.Setup(r => r.PollingRequestAsync(It.IsAny<Selector>()))
                    .ThrowsAsync(exception);

                using (var dataSource = MakeDataSource())
                {
                    _ = dataSource.Start();

                    var status = _updateSink.StatusUpdates.ExpectValue();
                    Assert.Equal(DataSourceState.Interrupted, status.State);
                    Assert.NotNull(status.LastError);
                    Assert.Equal(DataSourceStatus.ErrorKind.ErrorResponse, status.LastError.Value.Kind);
                    Assert.Equal(503, status.LastError.Value.StatusCode);
                    Assert.True(status.LastError.Value.FDv1Fallback, "FDv1Fallback should be true when fallback header is present");

                    Assert.False(dataSource.Initialized);
                }
            }
        }

        [Fact]
        public async Task UnrecoverableHttpErrorWithFallbackHeaderSetsFDv1Fallback()
        {
            // Create an HttpResponseMessage with the fallback header
            using (var response = new HttpResponseMessage((HttpStatusCode)401))
            {
                response.Headers.Add("x-ld-fd-fallback", "true");
                var exception = new UnsuccessfulResponseException(401, response.Headers);

                _mockRequestor.Setup(r => r.PollingRequestAsync(It.IsAny<Selector>()))
                    .ThrowsAsync(exception);

                using (var dataSource = MakeDataSource())
                {
                    var startTask = dataSource.Start();

                    var result = await startTask;
                    Assert.False(result);

                    var status = _updateSink.StatusUpdates.ExpectValue();
                    Assert.Equal(DataSourceState.Off, status.State);
                    Assert.NotNull(status.LastError);
                    Assert.Equal(DataSourceStatus.ErrorKind.ErrorResponse, status.LastError.Value.Kind);
                    Assert.Equal(401, status.LastError.Value.StatusCode);
                    Assert.True(status.LastError.Value.FDv1Fallback, "FDv1Fallback should be true when fallback header is present");

                    Assert.False(dataSource.Initialized);
                }
            }
        }

        [Fact]
        public void RecoverableHttpErrorWithoutFallbackHeaderDoesNotSetFDv1Fallback()
        {
            // Create an HttpResponseMessage without the fallback header
            using (var response = new HttpResponseMessage((HttpStatusCode)503))
            {
                var exception = new UnsuccessfulResponseException(503, response.Headers);

                _mockRequestor.Setup(r => r.PollingRequestAsync(It.IsAny<Selector>()))
                    .ThrowsAsync(exception);

                using (var dataSource = MakeDataSource())
                {
                    _ = dataSource.Start();

                    var status = _updateSink.StatusUpdates.ExpectValue();
                    Assert.Equal(DataSourceState.Interrupted, status.State);
                    Assert.NotNull(status.LastError);
                    Assert.Equal(DataSourceStatus.ErrorKind.ErrorResponse, status.LastError.Value.Kind);
                    Assert.Equal(503, status.LastError.Value.StatusCode);
                    Assert.False(status.LastError.Value.FDv1Fallback, "FDv1Fallback should be false when fallback header is not present");

                    Assert.False(dataSource.Initialized);
                }
            }
        }

        [Fact]
        public void RecoverableHttpErrorWithFallbackHeaderFalseDoesNotSetFDv1Fallback()
        {
            // Create an HttpResponseMessage with the fallback header set to false
            using (var response = new HttpResponseMessage((HttpStatusCode)503))
            {
                response.Headers.Add("x-ld-fd-fallback", "false");
                var exception = new UnsuccessfulResponseException(503, response.Headers);

                _mockRequestor.Setup(r => r.PollingRequestAsync(It.IsAny<Selector>()))
                    .ThrowsAsync(exception);

                using (var dataSource = MakeDataSource())
                {
                    _ = dataSource.Start();

                    var status = _updateSink.StatusUpdates.ExpectValue();
                    Assert.Equal(DataSourceState.Interrupted, status.State);
                    Assert.NotNull(status.LastError);
                    Assert.Equal(DataSourceStatus.ErrorKind.ErrorResponse, status.LastError.Value.Kind);
                    Assert.Equal(503, status.LastError.Value.StatusCode);
                    Assert.False(status.LastError.Value.FDv1Fallback, "FDv1Fallback should be false when fallback header value is false");

                    Assert.False(dataSource.Initialized);
                }
            }
        }

        [Fact]
        public async Task UnrecoverableHttpErrorWithFallbackHeaderFalseDoesNotSetFDv1Fallback()
        {
            // Create an HttpResponseMessage with the fallback header set to false
            using (var response = new HttpResponseMessage((HttpStatusCode)401))
            {
                response.Headers.Add("x-ld-fd-fallback", "false");
                var exception = new UnsuccessfulResponseException(401, response.Headers);

                _mockRequestor.Setup(r => r.PollingRequestAsync(It.IsAny<Selector>()))
                    .ThrowsAsync(exception);

                using (var dataSource = MakeDataSource())
                {
                    var startTask = dataSource.Start();

                    var result = await startTask;
                    Assert.False(result);

                    var status = _updateSink.StatusUpdates.ExpectValue();
                    Assert.Equal(DataSourceState.Off, status.State);
                    Assert.NotNull(status.LastError);
                    Assert.Equal(DataSourceStatus.ErrorKind.ErrorResponse, status.LastError.Value.Kind);
                    Assert.Equal(401, status.LastError.Value.StatusCode);
                    Assert.False(status.LastError.Value.FDv1Fallback, "FDv1Fallback should be false when fallback header value is false");

                    Assert.False(dataSource.Initialized);
                }
            }
        }
    }
}
