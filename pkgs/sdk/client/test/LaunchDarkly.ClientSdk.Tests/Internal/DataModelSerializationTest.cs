using LaunchDarkly.Sdk.Json;
using Xunit;

using static LaunchDarkly.Sdk.Client.TestUtil;
using static LaunchDarkly.TestHelpers.JsonAssertions;

namespace LaunchDarkly.Sdk.Client.Internal
{
    public class DataModelSerializationTest
    {
        [Fact]
        public void SerializeContext()
        {
            var user = Context.Builder("user-key")
                .Set("firstName", "Lucy").Set("lastName", "Cat").Build();
            AssertJsonEqual(LdJsonSerialization.SerializeObject(user),
                DataModelSerialization.SerializeContext(user));
        }

        [Fact]
        public void SerializeFlagWithMinimalProperties()
        {
            var flag = new FeatureFlagBuilder()
                .Version(1)
                .Value(LdValue.Of(false))
                .Build();
            AssertJsonEqual(@"{""version"":1,""value"":false}",
                DataModelSerialization.SerializeFlag(flag));
        }

        [Fact]
        public void SerializeFlagWithAllProperties()
        {
            var flag1 = new FeatureFlagBuilder()
                .Version(1)
                .Value(LdValue.Of(false))
                .Variation(2)
                .FlagVersion(3)
                .Reason(EvaluationReason.OffReason)
                .TrackEvents(true)
                .DebugEventsUntilDate(UnixMillisecondTime.OfMillis(1234))
                .Build();
            AssertJsonEqual(@"{""version"":1,""value"":false,""variation"":2,""flagVersion"":3," +
                @"""reason"":{""kind"":""OFF""},""trackEvents"":true,""debugEventsUntilDate"":1234}",
                DataModelSerialization.SerializeFlag(flag1));

            // make sure we're treating trackReason separately from trackEvents
            var flag2 = new FeatureFlagBuilder()
                .Version(1)
                .Value(LdValue.Of(false))
                .Reason(EvaluationReason.OffReason)
                .Variation(2)
                .FlagVersion(3)
                .TrackReason(true)
                .Build();
            AssertJsonEqual(@"{""version"":1,""value"":false,""variation"":2,""flagVersion"":3," +
                @"""reason"":{""kind"":""OFF""},""trackReason"":true}",
                DataModelSerialization.SerializeFlag(flag2));
        }

        [Fact]
        public void SerializeAll()
        {
            var flag1 = new FeatureFlagBuilder().Version(100).Value(LdValue.Of(false)).Build();
            var flag2 = new FeatureFlagBuilder().Version(200).Value(LdValue.Of(true)).Build();
            var flag1Json = DataModelSerialization.SerializeFlag(flag1);
            var flag2Json = DataModelSerialization.SerializeFlag(flag2);
            var deletedVersion = 300;
            var allData = new DataSetBuilder()
                .Add("key1", flag1)
                .Add("key2", flag2)
                .AddDeleted("deletedKey", deletedVersion)
                .Build();
            var actual = DataModelSerialization.SerializeAll(allData);
            var expected = MakeJsonData(allData);
            AssertJsonEqual(expected, actual);
        }

        [Fact]
        public void DeserializeAll()
        {
            var flag1 = new FeatureFlagBuilder().Version(100).Value(LdValue.Of(false)).Build();
            var flag2 = new FeatureFlagBuilder().Version(200).Value(LdValue.Of(true)).Build();
            var expectedData = new DataSetBuilder()
                .Add("key1", flag1)
                .Add("key2", flag2)
                .Build();
            var serialized = MakeJsonData(expectedData);

            var actualData1 = DataModelSerialization.DeserializeV1Schema(serialized);
            Assert.Equal(expectedData, actualData1);

            var actualData2 = DataModelSerialization.DeserializeAll(serialized);
            Assert.Equal(expectedData, actualData2);
        }
    }
}
