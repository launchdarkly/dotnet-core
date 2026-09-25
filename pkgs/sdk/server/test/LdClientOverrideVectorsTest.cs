using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LaunchDarkly.Sdk.Json;
using LaunchDarkly.Sdk.Server.Internal.Model;
using Xunit;
using Xunit.Abstractions;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server
{
    // Runs the OVERRIDE specification's test vectors. Each vector sets up LaunchDarkly data, an
    // override layer, and an initialization state, evaluates one flag through the full client stack,
    // and checks the value, the variation index, and the reason.
    public class LdClientOverrideVectorsTest : BaseTest
    {
        private static readonly string VectorsPath = TestUtils.TestFilePath("override-vectors.json");

        // The vectors' semantics are versioned. A schema change means this runner needs review.
        private const string SupportedSchemaVersion = "0.4.0";

        public LdClientOverrideVectorsTest(ITestOutputHelper testOutput) : base(testOutput) { }

        private static LdValue LoadVectorFile() => LdValue.Parse(File.ReadAllText(VectorsPath));

        public static IEnumerable<object[]> Vectors()
        {
            var file = LoadVectorFile();
            var vectors = file.Get("vectors").List;
            for (var i = 0; i < vectors.Count; i++)
            {
                yield return new object[] { i, vectors[i].Get("group").AsString + ": " + vectors[i].Get("description").AsString };
            }
        }

        [Fact]
        public void VectorFileHasTheSupportedSchema()
        {
            var file = LoadVectorFile();
            Assert.Equal(SupportedSchemaVersion, file.Get("schemaVersion").AsString);
            Assert.NotEmpty(file.Get("vectors").List);
        }

        [Theory]
        [MemberData(nameof(Vectors))]
        public void Vector(int index, string description)
        {
            var vector = LoadVectorFile().Get("vectors").List[index];
            Assert.NotNull(description);

            var launchDarklyData = vector.Get("launchDarklyData");
            var initialized = launchDarklyData.Get("initialized").AsBool;
            var ldDataSet = DataSetFrom(launchDarklyData.Get("flags"), launchDarklyData.Get("segments"), LdValue.Null);
            var overrides = vector.Get("overrides");
            var overrideDataSet = DataSetFrom(overrides.Get("flags"), overrides.Get("segments"), overrides.Get("flagValues"));

            var source = new TestOverrideSource(overrideDataSet);
            var dataSystem = Components.DataSystem().Custom().Overrides(source);
            var config = BasicConfig();
            if (initialized)
            {
                dataSystem.Synchronizers(MockComponents.MockDataSourceWithData(ldDataSet));
                config.StartWaitTime(TimeSpan.FromSeconds(5));
            }
            else
            {
                // With no data source at all, the data system would report cached data as available.
                // A synchronizer that never delivers anything keeps the client uninitialized.
                dataSystem.Synchronizers(MockComponents.MockDataSourceThatNeverStarts());
            }
            config.DataSystem(dataSystem);

            using (var client = new LdClient(config.Build()))
            {
                Assert.Equal(initialized, client.Initialized);

                var evaluate = vector.Get("evaluate");
                var evalContext = LdJsonSerialization.DeserializeObject<Context>(evaluate.Get("context").ToJsonString());
                var detail = client.JsonVariationDetail(evaluate.Get("flagKey").AsString, evalContext, evaluate.Get("defaultValue"));

                var expect = vector.Get("expect");
                Assert.Equal(expect.Get("value"), detail.Value);
                var expectedVariation = expect.Get("variationIndex");
                if (expectedVariation.IsNull)
                {
                    Assert.Null(detail.VariationIndex);
                }
                else
                {
                    Assert.Equal(expectedVariation.AsInt, detail.VariationIndex);
                }
                AssertReason(expect.Get("reason"), detail.Reason);
            }
        }

        // Compares the actual reason against only the fields present in the expected reason. The
        // override-affected indicator collapses tri-state: an expected reason that omits it requires
        // the actual reason to report false, which is never serialized, or omit it.
        private static void AssertReason(LdValue expected, EvaluationReason actual)
        {
            var actualJson = LdValue.Parse(LdJsonSerialization.SerializeObject(actual));
            foreach (var field in expected.Dictionary)
            {
                Assert.True(field.Value.Equals(actualJson.Get(field.Key)),
                    "reason field " + field.Key + ": expected " + field.Value + " but was " + actualJson.Get(field.Key));
            }
            if (expected.Get("overrideAffected").IsNull)
            {
                var actualMarker = actualJson.Get("overrideAffected");
                Assert.True(actualMarker.IsNull || !actualMarker.AsBool, "overrideAffected must be false or omitted");
                Assert.False(actual.OverrideAffected);
            }
        }

        private static FullDataSet<ItemDescriptor> DataSetFrom(LdValue flags, LdValue segments, LdValue flagValues)
        {
            var flagItems = new List<KeyValuePair<string, ItemDescriptor>>();
            foreach (var kv in flags.Dictionary)
            {
                flagItems.Add(new KeyValuePair<string, ItemDescriptor>(kv.Key, DataModel.Features.Deserialize(kv.Value.ToJsonString())));
            }
            foreach (var kv in flagValues.Dictionary)
            {
                // A value-only entry behaves like a flag that is off and serves its single value.
                var flag = new FeatureFlagBuilder(kv.Key).OffWithValue(kv.Value).Build();
                flagItems.Add(new KeyValuePair<string, ItemDescriptor>(kv.Key, new ItemDescriptor(flag.Version, flag)));
            }
            var segmentItems = new List<KeyValuePair<string, ItemDescriptor>>();
            foreach (var kv in segments.Dictionary)
            {
                segmentItems.Add(new KeyValuePair<string, ItemDescriptor>(kv.Key, DataModel.Segments.Deserialize(kv.Value.ToJsonString())));
            }
            return new FullDataSet<ItemDescriptor>(new[]
            {
                new KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>(DataModel.Features, new KeyedItems<ItemDescriptor>(flagItems)),
                new KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>(DataModel.Segments, new KeyedItems<ItemDescriptor>(segmentItems))
            });
        }
    }
}
