using System;
using System.Linq;
using System.Text.Json;
using LaunchDarkly.Sdk.Server.Internal.Model;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2Payloads
{
    public class FDv2PayloadsTest
    {
        private static JsonSerializerOptions GetJsonOptions()
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(ServerIntentConverter.Instance);
            options.Converters.Add(PutObjectConverter.Instance);
            options.Converters.Add(DeleteObjectConverter.Instance);
            options.Converters.Add(PayloadTransferredConverter.Instance);
            options.Converters.Add(ErrorConverter.Instance);
            options.Converters.Add(GoodbyeConverter.Instance);
            options.Converters.Add(FDv2PollEventConverter.Instance);
            options.Converters.Add(FeatureFlagSerialization.Instance);
            options.Converters.Add(SegmentSerialization.Instance);
            return options;
        }

        [Fact]
        public void ServerIntent_CanDeserializeAndReserialize()
        {
            const string json = @"{
                ""payloads"": [
                    {
                        ""id"": ""payload-123"",
                        ""target"": 42,
                        ""intentCode"": ""xfer-full"",
                        ""reason"": ""payload-missing""
                    }
                ]
            }";

            var serverIntent = JsonSerializer.Deserialize<ServerIntent>(json, GetJsonOptions());

            Assert.NotNull(serverIntent);
            Assert.Single(serverIntent.Payloads);
            Assert.Equal("payload-123", serverIntent.Payloads[0].Id);
            Assert.Equal(42, serverIntent.Payloads[0].Target);
            Assert.Equal(IntentCode.TransferFull, serverIntent.Payloads[0].IntentCode);
            Assert.Equal("payload-missing", serverIntent.Payloads[0].Reason);

            // Reserialize and verify
            var reserialized = JsonSerializer.Serialize(serverIntent, GetJsonOptions());
            var deserialized2 = JsonSerializer.Deserialize<ServerIntent>(reserialized, GetJsonOptions());
            Assert.Equal("payload-123", deserialized2.Payloads[0].Id);
            Assert.Equal(42, deserialized2.Payloads[0].Target);
            Assert.Equal(IntentCode.TransferFull, deserialized2.Payloads[0].IntentCode);
            Assert.Equal("payload-missing", deserialized2.Payloads[0].Reason);
        }

        [Fact]
        public void ServerIntent_CanDeserializeMultiplePayloads()
        {
            const string json = @"{
                ""payloads"": [
                    {
                        ""id"": ""payload-1"",
                        ""target"": 10,
                        ""intentCode"": ""xfer-changes"",
                        ""reason"": ""stale""
                    },
                    {
                        ""id"": ""payload-2"",
                        ""target"": 20,
                        ""intentCode"": ""none"",
                        ""reason"": ""up-to-date""
                    }
                ]
            }";

            var serverIntent = JsonSerializer.Deserialize<ServerIntent>(json, GetJsonOptions());

            Assert.NotNull(serverIntent);
            Assert.Equal(2, serverIntent.Payloads.Count);
            Assert.Equal("payload-1", serverIntent.Payloads[0].Id);
            Assert.Equal(IntentCode.TransferChanges, serverIntent.Payloads[0].IntentCode);
            Assert.Equal("payload-2", serverIntent.Payloads[1].Id);
            Assert.Equal(IntentCode.None, serverIntent.Payloads[1].IntentCode);
        }

        [Fact]
        public void PutObject_CanDeserializeWithFlag()
        {
            const string json = @"{
                ""version"": 10,
                ""kind"": ""flag"",
                ""key"": ""test-flag"",
                ""object"": {
                    ""key"": ""test-flag"",
                    ""version"": 5,
                    ""on"": true,
                    ""fallthrough"": { ""variation"": 0 },
                    ""offVariation"": 1,
                    ""variations"": [true, false],
                    ""salt"": ""abc123"",
                    ""trackEvents"": false,
                    ""trackEventsFallthrough"": false,
                    ""debugEventsUntilDate"": null,
                    ""clientSide"": true,
                    ""deleted"": false
                }
            }";

            var putObject = JsonSerializer.Deserialize<PutObject>(json, GetJsonOptions());

            Assert.NotNull(putObject);
            Assert.Equal(10, putObject.Version);
            Assert.Equal("flag", putObject.Kind);
            Assert.Equal("test-flag", putObject.Key);
            Assert.Equal(JsonValueKind.Object, putObject.Object.ValueKind);

            // Deserialize the flag from the Object element
            var flag = JsonSerializer.Deserialize<FeatureFlag>(putObject.Object.GetRawText(), GetJsonOptions());
            Assert.Equal("test-flag", flag.Key);
            Assert.Equal(5, flag.Version);
            Assert.True(flag.On);
            Assert.Equal("abc123", flag.Salt);
        }

        [Fact]
        public void PutObject_CanReserializeWithFlag()
        {
            var flag = new FeatureFlagBuilder("my-flag")
                .Version(3)
                .On(true)
                .Variations(LdValue.Of(true), LdValue.Of(false))
                .FallthroughVariation(0)
                .OffVariation(1)
                .Salt("salt123")
                .ClientSide(true)
                .Build();

            var flagJson = JsonSerializer.Serialize(flag, GetJsonOptions());
            var flagElement = JsonDocument.Parse(flagJson).RootElement;

            var putObject = new PutObject(15, "flag", "my-flag", flagElement);

            var serialized = JsonSerializer.Serialize(putObject, GetJsonOptions());
            var deserialized = JsonSerializer.Deserialize<PutObject>(serialized, GetJsonOptions());

            Assert.Equal(15, deserialized.Version);
            Assert.Equal("flag", deserialized.Kind);
            Assert.Equal("my-flag", deserialized.Key);

            var deserializedFlag =
                JsonSerializer.Deserialize<FeatureFlag>(deserialized.Object.GetRawText(), GetJsonOptions());
            Assert.Equal("my-flag", deserializedFlag.Key);
            Assert.Equal(3, deserializedFlag.Version);
            Assert.True(deserializedFlag.On);
            Assert.Equal("salt123", deserializedFlag.Salt);
            Assert.True(deserializedFlag.ClientSide);
            Assert.Equal(0, deserializedFlag.Fallthrough.Variation);
            Assert.Equal(1, deserializedFlag.OffVariation);
            Assert.Equal(2, deserializedFlag.Variations.Count());
            Assert.True(deserializedFlag.Variations.ElementAt(0).AsBool);
            Assert.False(deserializedFlag.Variations.ElementAt(1).AsBool);
        }

        [Fact]
        public void PutObject_CanDeserializeWithSegment()
        {
            const string json = @"{
                ""version"": 20,
                ""kind"": ""segment"",
                ""key"": ""test-segment"",
                ""object"": {
                    ""key"": ""test-segment"",
                    ""version"": 7,
                    ""included"": [""user1"", ""user2""],
                    ""salt"": ""seg-salt"",
                    ""deleted"": false
                }
            }";

            var putObject = JsonSerializer.Deserialize<PutObject>(json, GetJsonOptions());

            Assert.NotNull(putObject);
            Assert.Equal(20, putObject.Version);
            Assert.Equal("segment", putObject.Kind);
            Assert.Equal("test-segment", putObject.Key);

            // Deserialize the segment from the Object element
            var segment = JsonSerializer.Deserialize<Segment>(putObject.Object.GetRawText(), GetJsonOptions());
            Assert.Equal("test-segment", segment.Key);
            Assert.Equal(7, segment.Version);
            Assert.Equal(2, segment.Included.Count);
            Assert.Contains("user1", segment.Included);
            Assert.Contains("user2", segment.Included);
        }

        [Fact]
        public void PutObject_CanReserializeWithSegment()
        {
            var segment = new SegmentBuilder("my-segment")
                .Version(5)
                .Included("alice", "bob")
                .Salt("segment-salt")
                .Build();

            var segmentJson = JsonSerializer.Serialize(segment, GetJsonOptions());
            var segmentElement = JsonDocument.Parse(segmentJson).RootElement;

            var putObject = new PutObject(25, "segment", "my-segment", segmentElement);

            var serialized = JsonSerializer.Serialize(putObject, GetJsonOptions());
            var deserialized = JsonSerializer.Deserialize<PutObject>(serialized, GetJsonOptions());

            Assert.Equal(25, deserialized.Version);
            Assert.Equal("segment", deserialized.Kind);
            Assert.Equal("my-segment", deserialized.Key);

            var deserializedSegment =
                JsonSerializer.Deserialize<Segment>(deserialized.Object.GetRawText(), GetJsonOptions());
            Assert.Equal("my-segment", deserializedSegment.Key);
            Assert.Equal(5, deserializedSegment.Version);
            Assert.Equal(2, deserializedSegment.Included.Count);
            Assert.Contains("alice", deserializedSegment.Included);
            Assert.Contains("bob", deserializedSegment.Included);
            Assert.Equal("segment-salt", deserializedSegment.Salt);
        }

        [Fact]
        public void DeleteObject_CanDeserializeAndReserialize()
        {
            const string json = @"{
                ""version"": 30,
                ""kind"": ""flag"",
                ""key"": ""deleted-flag""
            }";

            var deleteObject = JsonSerializer.Deserialize<DeleteObject>(json, GetJsonOptions());

            Assert.NotNull(deleteObject);
            Assert.Equal(30, deleteObject.Version);
            Assert.Equal("flag", deleteObject.Kind);
            Assert.Equal("deleted-flag", deleteObject.Key);

            // Reserialize
            var reserialized = JsonSerializer.Serialize(deleteObject, GetJsonOptions());
            var deserialized2 = JsonSerializer.Deserialize<DeleteObject>(reserialized, GetJsonOptions());
            Assert.Equal(30, deserialized2.Version);
            Assert.Equal("flag", deserialized2.Kind);
            Assert.Equal("deleted-flag", deserialized2.Key);
        }

        [Fact]
        public void DeleteObject_CanDeserializeSegment()
        {
            const string json = @"{
                ""version"": 12,
                ""kind"": ""segment"",
                ""key"": ""removed-segment""
            }";

            var deleteObject = JsonSerializer.Deserialize<DeleteObject>(json, GetJsonOptions());

            Assert.Equal(12, deleteObject.Version);
            Assert.Equal("segment", deleteObject.Kind);
            Assert.Equal("removed-segment", deleteObject.Key);
        }

        [Fact]
        public void PayloadTransferred_CanDeserializeAndReserialize()
        {
            const string json = @"{
                ""state"": ""(p:ABC123:42)"",
                ""version"": 42
            }";

            var payloadTransferred = JsonSerializer.Deserialize<PayloadTransferred>(json, GetJsonOptions());

            Assert.NotNull(payloadTransferred);
            Assert.Equal("(p:ABC123:42)", payloadTransferred.State);
            Assert.Equal(42, payloadTransferred.Version);

            // Reserialize
            var reserialized = JsonSerializer.Serialize(payloadTransferred, GetJsonOptions());
            var deserialized2 = JsonSerializer.Deserialize<PayloadTransferred>(reserialized, GetJsonOptions());
            Assert.Equal("(p:ABC123:42)", deserialized2.State);
            Assert.Equal(42, deserialized2.Version);
        }

        [Fact]
        public void Error_CanDeserializeAndReserialize()
        {
            const string json = @"{
                ""id"": ""error-123"",
                ""reason"": ""Something went wrong""
            }";

            var error = JsonSerializer.Deserialize<Error>(json, GetJsonOptions());

            Assert.NotNull(error);
            Assert.Equal("error-123", error.Id);
            Assert.Equal("Something went wrong", error.Reason);

            // Reserialize
            var reserialized = JsonSerializer.Serialize(error, GetJsonOptions());
            var deserialized2 = JsonSerializer.Deserialize<Error>(reserialized, GetJsonOptions());
            Assert.Equal("error-123", deserialized2.Id);
            Assert.Equal("Something went wrong", deserialized2.Reason);
        }

        [Fact]
        public void Goodbye_CanDeserializeAndReserialize()
        {
            const string json = @"{
                ""reason"": ""Server is shutting down""
            }";

            var goodbye = JsonSerializer.Deserialize<Goodbye>(json, GetJsonOptions());

            Assert.NotNull(goodbye);
            Assert.Equal("Server is shutting down", goodbye.Reason);

            // Reserialize
            var reserialized = JsonSerializer.Serialize(goodbye, GetJsonOptions());
            var deserialized2 = JsonSerializer.Deserialize<Goodbye>(reserialized, GetJsonOptions());
            Assert.Equal("Server is shutting down", deserialized2.Reason);
        }

        [Fact]
        public void FDv2PollEvent_CanDeserializeServerIntent()
        {
            const string json = @"{
                ""event"": ""server-intent"",
                ""data"": {
                    ""payloads"": [
                        {
                            ""id"": ""evt-123"",
                            ""target"": 50,
                            ""intentCode"": ""xfer-full"",
                            ""reason"": ""payload-missing""
                        }
                    ]
                }
            }";

            var pollEvent = JsonSerializer.Deserialize<FDv2Event>(json, GetJsonOptions());

            Assert.NotNull(pollEvent);
            Assert.Equal("server-intent", pollEvent.EventType);

            var serverIntent = pollEvent.AsServerIntent();
            Assert.NotNull(serverIntent);
            Assert.Single(serverIntent.Payloads);
            Assert.Equal("evt-123", serverIntent.Payloads[0].Id);
            Assert.Equal(50, serverIntent.Payloads[0].Target);
        }

        [Fact]
        public void FDv2PollEvent_CanDeserializePutObject()
        {
            const string json = @"{
                ""event"": ""put-object"",
                ""data"": {
                    ""version"": 100,
                    ""kind"": ""flag"",
                    ""key"": ""event-flag"",
                    ""object"": {
                        ""key"": ""event-flag"",
                        ""version"": 1,
                        ""on"": false,
                        ""fallthrough"": { ""variation"": 1 },
                        ""offVariation"": 1,
                        ""variations"": [""A"", ""B"", ""C""],
                        ""salt"": ""evt-salt"",
                        ""trackEvents"": false,
                        ""trackEventsFallthrough"": false,
                        ""debugEventsUntilDate"": null,
                        ""clientSide"": false,
                        ""deleted"": false
                    }
                }
            }";

            var pollEvent = JsonSerializer.Deserialize<FDv2Event>(json, GetJsonOptions());

            Assert.NotNull(pollEvent);
            Assert.Equal("put-object", pollEvent.EventType);

            var putObject = pollEvent.AsPutObject();
            Assert.NotNull(putObject);
            Assert.Equal(100, putObject.Version);
            Assert.Equal("flag", putObject.Kind);
            Assert.Equal("event-flag", putObject.Key);

            var flag = JsonSerializer.Deserialize<FeatureFlag>(putObject.Object.GetRawText(), GetJsonOptions());
            Assert.Equal("event-flag", flag.Key);
            Assert.False(flag.On);
            Assert.Equal(3, flag.Variations.Count());
        }

        [Fact]
        public void FDv2PollEvent_CanDeserializeDeleteObject()
        {
            const string json = @"{
                ""event"": ""delete-object"",
                ""data"": {
                    ""version"": 99,
                    ""kind"": ""segment"",
                    ""key"": ""old-segment""
                }
            }";

            var pollEvent = JsonSerializer.Deserialize<FDv2Event>(json, GetJsonOptions());

            Assert.Equal("delete-object", pollEvent.EventType);

            var deleteObject = pollEvent.AsDeleteObject();
            Assert.Equal(99, deleteObject.Version);
            Assert.Equal("segment", deleteObject.Kind);
            Assert.Equal("old-segment", deleteObject.Key);
        }

        [Fact]
        public void FDv2PollEvent_CanDeserializePayloadTransferred()
        {
            const string json = @"{
                ""event"": ""payload-transferred"",
                ""data"": {
                    ""state"": ""(p:XYZ789:100)"",
                    ""version"": 100
                }
            }";

            var pollEvent = JsonSerializer.Deserialize<FDv2Event>(json, GetJsonOptions());

            Assert.Equal("payload-transferred", pollEvent.EventType);

            var payloadTransferred = pollEvent.AsPayloadTransferred();
            Assert.Equal("(p:XYZ789:100)", payloadTransferred.State);
            Assert.Equal(100, payloadTransferred.Version);
        }

        [Fact]
        public void ServerIntent_ThrowsWhenPayloadsFieldMissing()
        {
            const string json = @"{}";
            Assert.ThrowsAny<JsonException>(() =>
                JsonSerializer.Deserialize<ServerIntent>(json, GetJsonOptions()));
        }

        [Fact]
        public void ServerIntent_ThrowsWhenPayloadIdFieldMissing()
        {
            const string json = @"{
                ""payloads"": [
                    {
                        ""target"": 42,
                        ""intentCode"": ""xfer-full"",
                        ""reason"": ""payload-missing""
                    }
                ]
            }";
            Assert.ThrowsAny<JsonException>(() =>
                JsonSerializer.Deserialize<ServerIntent>(json, GetJsonOptions()));
        }

        [Fact]
        public void ServerIntent_ThrowsWhenPayloadTargetFieldMissing()
        {
            const string json = @"{
                ""payloads"": [
                    {
                        ""id"": ""payload-123"",
                        ""intentCode"": ""xfer-full"",
                        ""reason"": ""payload-missing""
                    }
                ]
            }";
            Assert.ThrowsAny<JsonException>(() =>
                JsonSerializer.Deserialize<ServerIntent>(json, GetJsonOptions()));
        }

        [Fact]
        public void ServerIntent_ThrowsWhenPayloadIntentCodeFieldMissing()
        {
            const string json = @"{
                ""payloads"": [
                    {
                        ""id"": ""payload-123"",
                        ""target"": 42,
                        ""reason"": ""payload-missing""
                    }
                ]
            }";
            Assert.ThrowsAny<JsonException>(() =>
                JsonSerializer.Deserialize<ServerIntent>(json, GetJsonOptions()));
        }

        [Fact]
        public void ServerIntent_ThrowsWhenPayloadReasonFieldMissing()
        {
            const string json = @"{
                ""payloads"": [
                    {
                        ""id"": ""payload-123"",
                        ""target"": 42,
                        ""intentCode"": ""xfer-full""
                    }
                ]
            }";
            Assert.ThrowsAny<JsonException>(() =>
                JsonSerializer.Deserialize<ServerIntent>(json, GetJsonOptions()));
        }

        [Fact]
        public void PutObject_ThrowsWhenVersionFieldMissing()
        {
            const string json = @"{
                ""kind"": ""flag"",
                ""key"": ""test-flag"",
                ""object"": {}
            }";
            Assert.ThrowsAny<JsonException>(() =>
                JsonSerializer.Deserialize<PutObject>(json, GetJsonOptions()));
        }

        [Fact]
        public void PutObject_ThrowsWhenKindFieldMissing()
        {
            const string json = @"{
                ""version"": 10,
                ""key"": ""test-flag"",
                ""object"": {}
            }";
            Assert.ThrowsAny<JsonException>(() =>
                JsonSerializer.Deserialize<PutObject>(json, GetJsonOptions()));
        }

        [Fact]
        public void PutObject_ThrowsWhenKeyFieldMissing()
        {
            const string json = @"{
                ""version"": 10,
                ""kind"": ""flag"",
                ""object"": {}
            }";
            Assert.ThrowsAny<JsonException>(() =>
                JsonSerializer.Deserialize<PutObject>(json, GetJsonOptions()));
        }

        [Fact]
        public void PutObject_ThrowsWhenObjectFieldMissing()
        {
            const string json = @"{
                ""version"": 10,
                ""kind"": ""flag"",
                ""key"": ""test-flag""
            }";
            Assert.ThrowsAny<JsonException>(() =>
                JsonSerializer.Deserialize<PutObject>(json, GetJsonOptions()));
        }

        [Fact]
        public void DeleteObject_ThrowsWhenVersionFieldMissing()
        {
            const string json = @"{
                ""kind"": ""flag"",
                ""key"": ""test-flag""
            }";
            Assert.ThrowsAny<JsonException>(() =>
                JsonSerializer.Deserialize<DeleteObject>(json, GetJsonOptions()));
        }

        [Fact]
        public void DeleteObject_ThrowsWhenKindFieldMissing()
        {
            const string json = @"{
                ""version"": 30,
                ""key"": ""test-flag""
            }";
            Assert.ThrowsAny<JsonException>(() =>
                JsonSerializer.Deserialize<DeleteObject>(json, GetJsonOptions()));
        }

        [Fact]
        public void DeleteObject_ThrowsWhenKeyFieldMissing()
        {
            const string json = @"{
                ""version"": 30,
                ""kind"": ""flag""
            }";
            Assert.ThrowsAny<JsonException>(() =>
                JsonSerializer.Deserialize<DeleteObject>(json, GetJsonOptions()));
        }

        [Fact]
        public void PayloadTransferred_ThrowsWhenStateFieldMissing()
        {
            const string json = @"{
                ""version"": 42
            }";
            Assert.ThrowsAny<JsonException>(() =>
                JsonSerializer.Deserialize<PayloadTransferred>(json, GetJsonOptions()));
        }

        [Fact]
        public void PayloadTransferred_ThrowsWhenVersionFieldMissing()
        {
            const string json = @"{
                ""state"": ""(p:ABC123:42)""
            }";
            Assert.ThrowsAny<JsonException>(() =>
                JsonSerializer.Deserialize<PayloadTransferred>(json, GetJsonOptions()));
        }

        [Fact]
        public void Error_ThrowsWhenReasonFieldMissing()
        {
            const string json = @"{
                ""id"": ""error-123""
            }";
            Assert.ThrowsAny<JsonException>(() =>
                JsonSerializer.Deserialize<Error>(json, GetJsonOptions()));
        }

        [Fact]
        public void Goodbye_CanDeserializeWithoutReason()
        {
            // Goodbye has no required fields, so an empty object should be valid
            const string json = @"{}";
            var goodbye = JsonSerializer.Deserialize<Goodbye>(json, GetJsonOptions());
            Assert.NotNull(goodbye);
            Assert.Null(goodbye.Reason);
        }

        [Fact]
        public void FDv2PollEvent_ThrowsWhenEventFieldMissing()
        {
            const string json = @"{
                ""data"": {
                    ""state"": ""(p:XYZ:100)"",
                    ""version"": 100
                }
            }";
            Assert.ThrowsAny<JsonException>(() =>
                JsonSerializer.Deserialize<FDv2Event>(json, GetJsonOptions()));
        }

        [Fact]
        public void FDv2PollEvent_ThrowsWhenDataFieldMissing()
        {
            const string json = @"{
                ""event"": ""payload-transferred""
            }";
            Assert.ThrowsAny<JsonException>(() =>
                JsonSerializer.Deserialize<FDv2Event>(json, GetJsonOptions()));
        }

        [Fact]
        public void ServerIntent_ThrowsArgumentNullExceptionWhenPayloadsIsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new ServerIntent(null));
        }

        [Fact]
        public void ServerIntentPayload_ThrowsArgumentNullExceptionWhenIdIsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new ServerIntentPayload(null, 42, IntentCode.TransferFull, "reason"));
        }

        [Fact]
        public void ServerIntentPayload_ThrowsArgumentNullExceptionWhenReasonIsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new ServerIntentPayload("id-123", 42, IntentCode.TransferFull, null));
        }

        [Fact]
        public void PutObject_ThrowsArgumentNullExceptionWhenKindIsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new PutObject(1, null, "key", default));
        }

        [Fact]
        public void PutObject_ThrowsArgumentNullExceptionWhenKeyIsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new PutObject(1, "flag", null, default));
        }

        [Fact]
        public void DeleteObject_ThrowsArgumentNullExceptionWhenKindIsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new DeleteObject(1, null, "key"));
        }

        [Fact]
        public void DeleteObject_ThrowsArgumentNullExceptionWhenKeyIsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new DeleteObject(1, "flag", null));
        }

        [Fact]
        public void PayloadTransferred_ThrowsArgumentNullExceptionWhenStateIsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new PayloadTransferred(null, 42));
        }

        [Fact]
        public void Error_ThrowsArgumentNullExceptionWhenReasonIsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new Error("id", null));
        }

        [Fact]
        public void FullPollingResponse_CanDeserialize()
        {
            const string json = @"{
                ""events"": [
                    {
                        ""event"": ""server-intent"",
                        ""data"": {
                            ""payloads"": [{
                                ""id"": ""poll-payload-1"",
                                ""target"": 200,
                                ""intentCode"": ""xfer-full"",
                                ""reason"": ""payload-missing""
                            }]
                        }
                    },
                    {
                        ""event"": ""put-object"",
                        ""data"": {
                            ""version"": 150,
                            ""kind"": ""flag"",
                            ""key"": ""flag-one"",
                            ""object"": {
                                ""key"": ""flag-one"",
                                ""version"": 1,
                                ""on"": true,
                                ""fallthrough"": { ""variation"": 0 },
                                ""offVariation"": 1,
                                ""variations"": [true, false],
                                ""salt"": ""flag-one-salt"",
                                ""trackEvents"": false,
                                ""trackEventsFallthrough"": false,
                                ""debugEventsUntilDate"": null,
                                ""clientSide"": true,
                                ""deleted"": false
                            }
                        }
                    },
                    {
                        ""event"": ""put-object"",
                        ""data"": {
                            ""version"": 160,
                            ""kind"": ""segment"",
                            ""key"": ""segment-one"",
                            ""object"": {
                                ""key"": ""segment-one"",
                                ""version"": 2,
                                ""included"": [""user-a"", ""user-b""],
                                ""salt"": ""seg-salt"",
                                ""deleted"": false
                            }
                        }
                    },
                    {
                        ""event"": ""delete-object"",
                        ""data"": {
                            ""version"": 170,
                            ""kind"": ""flag"",
                            ""key"": ""old-flag""
                        }
                    },
                    {
                        ""event"": ""payload-transferred"",
                        ""data"": {
                            ""state"": ""(p:poll-payload-1:200)"",
                            ""version"": 200
                        }
                    }
                ]
            }";

            // Parse the polling response
            using (var doc = JsonDocument.Parse(json))
            {
                var eventsArray = doc.RootElement.GetProperty("events");

                Assert.Equal(JsonValueKind.Array, eventsArray.ValueKind);
                Assert.Equal(5, eventsArray.GetArrayLength());

                var eventsList = new System.Collections.Generic.List<FDv2Event>();
                foreach (var eventElement in eventsArray.EnumerateArray())
                {
                    var pollEvent =
                        JsonSerializer.Deserialize<FDv2Event>(eventElement.GetRawText(), GetJsonOptions());
                    eventsList.Add(pollEvent);
                }

                // Verify server-intent
                Assert.Equal("server-intent", eventsList[0].EventType);
                var serverIntent = eventsList[0].AsServerIntent();
                Assert.Equal("poll-payload-1", serverIntent.Payloads[0].Id);
                Assert.Equal(200, serverIntent.Payloads[0].Target);

                // Verify first put-object (flag)
                Assert.Equal("put-object", eventsList[1].EventType);
                var putFlag = eventsList[1].AsPutObject();
                Assert.Equal("flag", putFlag.Kind);
                Assert.Equal("flag-one", putFlag.Key);
                var flag = JsonSerializer.Deserialize<FeatureFlag>(putFlag.Object.GetRawText(), GetJsonOptions());
                Assert.Equal("flag-one", flag.Key);
                Assert.True(flag.On);

                // Verify second put-object (segment)
                Assert.Equal("put-object", eventsList[2].EventType);
                var putSegment = eventsList[2].AsPutObject();
                Assert.Equal("segment", putSegment.Kind);
                Assert.Equal("segment-one", putSegment.Key);
                var segment = JsonSerializer.Deserialize<Segment>(putSegment.Object.GetRawText(), GetJsonOptions());
                Assert.Equal("segment-one", segment.Key);
                Assert.Equal(2, segment.Included.Count);

                // Verify delete-object
                Assert.Equal("delete-object", eventsList[3].EventType);
                var deleteObj = eventsList[3].AsDeleteObject();
                Assert.Equal("flag", deleteObj.Kind);
                Assert.Equal("old-flag", deleteObj.Key);

                // Verify payload-transferred
                Assert.Equal("payload-transferred", eventsList[4].EventType);
                var transferred = eventsList[4].AsPayloadTransferred();
                Assert.Equal("(p:poll-payload-1:200)", transferred.State);
                Assert.Equal(200, transferred.Version);
            }
        }
    }
}
