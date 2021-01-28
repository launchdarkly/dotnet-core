﻿using LaunchDarkly.JsonStream;
using LaunchDarkly.Sdk.Json;
using LaunchDarkly.Sdk.Server.Internal.Model;
using Xunit;

using static LaunchDarkly.Sdk.Server.Interfaces.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    public class StreamProcessorEventsTest
    {
        [Fact]
        public void ParsePutData()
        {
            var allDataJson = @"{
 ""flags"": {
  ""flag1"": { ""key"": ""flag1"", ""version"": 1},
  ""flag2"": { ""key"": ""flag2"", ""version"": 2}
 },
 ""segments"": {
  ""segment1"": {""key"": ""segment1"",""version"": 3}
 }
}";
            var expectedAllData = new DataSetBuilder()
                .Flags(new FeatureFlagBuilder("flag1").Version(1).Build(),
                    new FeatureFlagBuilder("flag2").Version(2).Build())
                .Segments(new SegmentBuilder("segment1").Version(3).Build())
                .Build();

            var validInput = @"{""path"": ""/"", ""data"": " + allDataJson + "}";
            var validResult = StreamProcessorEvents.ParsePutData(validInput);
            Assert.Equal("/", validResult.Path);
            AssertHelpers.DataSetsEqual(expectedAllData, validResult.Data);

            var inputWithoutPath = @"{""data"": " + allDataJson + "}";
            var resultWithoutPath = StreamProcessorEvents.ParsePutData(inputWithoutPath);
            Assert.Null(resultWithoutPath.Path); // we don't consider this an error; some versions of Relay don't send a path
            AssertHelpers.DataSetsEqual(expectedAllData, resultWithoutPath.Data);

            var inputWithoutData = @"{""path"": ""/""}";
            Assert.ThrowsAny<JsonReadException>(() => StreamProcessorEvents.ParsePutData(inputWithoutData));

            var malformedJsonInput = @"{no";
            Assert.ThrowsAny<JsonReadException>(() => StreamProcessorEvents.ParsePutData(malformedJsonInput));
        }

        [Fact]
        public void ParsePatchData()
        {
            var flag = new FeatureFlagBuilder("flagkey").Version(2).On(true).Build();
            var segment = new SegmentBuilder("segmentkey").Version(3).Included("x").Build();
            var flagJson = LdJsonSerialization.SerializeObject(flag);
            var segmentJson = LdJsonSerialization.SerializeObject(segment);

            var validFlagInput = @"{""path"": ""/flags/flagkey"", ""data"": " + flagJson + "}";
            var validFlagResult = StreamProcessorEvents.ParsePatchData(validFlagInput);
            Assert.Equal(DataModel.Features, validFlagResult.Kind);
            Assert.Equal("flagkey", validFlagResult.Key);
            AssertHelpers.DataItemsEqual(DataModel.Features, new ItemDescriptor(flag.Version, flag),
                validFlagResult.Item);

            var validSegmentInput = @"{""path"": ""/segments/segmentkey"", ""data"": " + segmentJson + "}";
            var validSegmentResult = StreamProcessorEvents.ParsePatchData(validSegmentInput);
            Assert.Equal(DataModel.Segments, validSegmentResult.Kind);
            Assert.Equal("segmentkey", validSegmentResult.Key);
            AssertHelpers.DataItemsEqual(DataModel.Segments, new ItemDescriptor(segment.Version, segment),
                validSegmentResult.Item);

            var validFlagInputWithDataBeforePath = @"{""data"": " + flagJson + @", ""path"": ""/flags/flagkey""}";
            var validFlagResultWithDataBeforePath = StreamProcessorEvents.ParsePatchData(validFlagInputWithDataBeforePath);
            Assert.Equal(DataModel.Features, validFlagResultWithDataBeforePath.Kind);
            Assert.Equal("flagkey", validFlagResultWithDataBeforePath.Key);
            AssertHelpers.DataItemsEqual(DataModel.Features, new ItemDescriptor(flag.Version, flag),
                validFlagResultWithDataBeforePath.Item);

            var inputWithUnrecognizedPath = @"{""path"": ""/cats/lucy"", ""data"": " + flagJson + "}";
            var resultWithUnrecognizedPath = StreamProcessorEvents.ParsePatchData(inputWithUnrecognizedPath);
            Assert.Null(resultWithUnrecognizedPath.Kind);
            Assert.Null(resultWithUnrecognizedPath.Key);

            var inputWithMissingPath = @"{""data"": " + flagJson + "}";
            Assert.ThrowsAny<JsonReadException>(() => StreamProcessorEvents.ParsePatchData(inputWithMissingPath));

            var inputWithMissingData = @"{""path"": ""/flags/flagkey""}";
            Assert.ThrowsAny<JsonReadException>(() => StreamProcessorEvents.ParsePatchData(inputWithMissingData));

            var malformedJsonInput = @"{no";
            Assert.ThrowsAny<JsonReadException>(() => StreamProcessorEvents.ParsePatchData(malformedJsonInput));
        }

        [Fact]
        public void ParseDeleteData()
        {
            var validFlagInput = @"{""path"": ""/flags/flagkey"", ""version"": 3}";
            var validFlagResult = StreamProcessorEvents.ParseDeleteData(validFlagInput);
            Assert.Equal(DataModel.Features, validFlagResult.Kind);
            Assert.Equal("flagkey", validFlagResult.Key);
            Assert.Equal(3, validFlagResult.Version);

            var validSegmentInput = @"{""path"": ""/segments/segmentkey"", ""version"": 4}";
            var validSegmentResult = StreamProcessorEvents.ParseDeleteData(validSegmentInput);
            Assert.Equal(DataModel.Segments, validSegmentResult.Kind);
            Assert.Equal("segmentkey", validSegmentResult.Key);
            Assert.Equal(4, validSegmentResult.Version);

            var inputWithUnrecognizedPath = @"{""path"": ""/cats/macavity"", ""version"": 9}";
            var resultWithUnrecognizedPath = StreamProcessorEvents.ParseDeleteData(inputWithUnrecognizedPath);
            Assert.Null(resultWithUnrecognizedPath.Kind);
            Assert.Null(resultWithUnrecognizedPath.Key);

            var inputWithMissingPath = @"{""version"": 1}";
            Assert.ThrowsAny<JsonReadException>(() => StreamProcessorEvents.ParseDeleteData(inputWithMissingPath));

            var inputWithMissingVersion = @"{""path"": ""/flags/flagkey""}";
            Assert.ThrowsAny<JsonReadException>(() => StreamProcessorEvents.ParseDeleteData(inputWithMissingVersion));

            var malformedJsonInput = @"{no";
            Assert.ThrowsAny<JsonReadException>(() => StreamProcessorEvents.ParseDeleteData(malformedJsonInput));
        }
    }
}
