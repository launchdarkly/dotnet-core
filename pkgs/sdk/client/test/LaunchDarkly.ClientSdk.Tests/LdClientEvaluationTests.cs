using System;
using LaunchDarkly.Sdk.Client.Integrations;
using Xunit;
using Xunit.Abstractions;

namespace LaunchDarkly.Sdk.Client
{
    public class LdClientEvaluationTests : BaseTest
    {
        const string flagKey = "flag-key";
        const string nonexistentFlagKey = "some flag key";
        static readonly Context user = Context.New("userkey");

        private readonly TestData _testData = TestData.DataSource();

        public LdClientEvaluationTests(ITestOutputHelper testOutput) : base(testOutput) { }

        private LdClient MakeClient() =>
            TestUtil.CreateClient(BasicConfig().DataSource(_testData).Build(), user, TimeSpan.FromSeconds(1));

        [Fact]
        public void BoolVariationReturnsValue()
        {
            _testData.Update(_testData.Flag(flagKey).Variation(true));
            using (var client = MakeClient())
            {
                Assert.True(client.BoolVariation(flagKey, false));
            }
        }

        [Fact]
        public void BoolVariationReturnsDefaultForUnknownFlag()
        {
            using (var client = MakeClient())
            {
                Assert.False(client.BoolVariation(nonexistentFlagKey));
            }
        }

        [Fact]
        public void BoolVariationDetailReturnsValue()
        {
            var reason = EvaluationReason.OffReason;
            var flag = new FeatureFlagBuilder().Value(true).Variation(1).Reason(reason).Build();
            _testData.Update(_testData.Flag(flagKey).PreconfiguredFlag(flag));
            using (var client = MakeClient())
            {
                var expected = new EvaluationDetail<bool>(true, 1, reason);
                Assert.Equal(expected, client.BoolVariationDetail(flagKey, false));
            }
        }

        [Fact]
        public void IntVariationReturnsValue()
        {
            _testData.Update(_testData.Flag(flagKey).Variation(LdValue.Of(3)));
            using (var client = MakeClient())
            {
                Assert.Equal(3, client.IntVariation(flagKey, 0));
            }
        }

        [Fact]
        public void IntVariationCoercesFloatValue()
        {
            _testData.Update(_testData.Flag(flagKey).Variation(LdValue.Of(3.25f)));
            using (var client = MakeClient())
            {
                Assert.Equal(3, client.IntVariation(flagKey, 0));
            }
        }

        [Fact]
        public void IntVariationReturnsDefaultForUnknownFlag()
        {
            using (var client = MakeClient())
            {
                Assert.Equal(1, client.IntVariation(nonexistentFlagKey, 1));
            }
        }

        [Fact]
        public void IntVariationDetailReturnsValue()
        {
            var reason = EvaluationReason.OffReason;
            var flag = new FeatureFlagBuilder().Value(LdValue.Of(3)).Variation(1).Reason(reason).Build();
            _testData.Update(_testData.Flag(flagKey).PreconfiguredFlag(flag));
            using (var client = MakeClient())
            {
                var expected = new EvaluationDetail<int>(3, 1, reason);
                Assert.Equal(expected, client.IntVariationDetail(flagKey, 0));
            }
        }

        [Fact]
        public void FloatVariationReturnsValue()
        {
            _testData.Update(_testData.Flag(flagKey).Variation(LdValue.Of(2.5f)));
            using (var client = MakeClient())
            {
                Assert.Equal(2.5f, client.FloatVariation(flagKey, 0));
            }
        }

        [Fact]
        public void FloatVariationCoercesIntValue()
        {
            _testData.Update(_testData.Flag(flagKey).Variation(LdValue.Of(2)));
            using (var client = MakeClient())
            {
                Assert.Equal(2.0f, client.FloatVariation(flagKey, 0));
            }
        }

        [Fact]
        public void FloatVariationReturnsDefaultForUnknownFlag()
        {
            using (var client = MakeClient())
            {
                Assert.Equal(0.5f, client.FloatVariation(nonexistentFlagKey, 0.5f));
            }
        }

        [Fact]
        public void FloatVariationDetailReturnsValue()
        {
            var reason = EvaluationReason.OffReason;
            var flag = new FeatureFlagBuilder().Value(LdValue.Of(2.5f)).Variation(1).Reason(reason).Build();
            _testData.Update(_testData.Flag(flagKey).PreconfiguredFlag(flag));
            using (var client = MakeClient())
            {
                var expected = new EvaluationDetail<float>(2.5f, 1, reason);
                Assert.Equal(expected, client.FloatVariationDetail(flagKey, 0.5f));
            }
        }

        [Fact]
        public void DoubleVariationReturnsValue()
        {
            _testData.Update(_testData.Flag(flagKey).Variation(LdValue.Of(2.5d)));
            using (var client = MakeClient())
            {
                Assert.Equal(2.5d, client.DoubleVariation(flagKey, 0));
            }
        }

        [Fact]
        public void DoubleVariationCoercesIntValue()
        {
            _testData.Update(_testData.Flag(flagKey).Variation(LdValue.Of(2)));
            using (var client = MakeClient())
            {
                Assert.Equal(2.0d, client.DoubleVariation(flagKey, 0));
            }
        }

        [Fact]
        public void DoubleVariationReturnsDefaultForUnknownFlag()
        {
            using (var client = MakeClient())
            {
                Assert.Equal(0.5d, client.DoubleVariation(nonexistentFlagKey, 0.5d));
            }
        }

        [Fact]
        public void DoubleVariationDetailReturnsValue()
        {
            var reason = EvaluationReason.OffReason;
            var flag = new FeatureFlagBuilder().Value(LdValue.Of(2.5d)).Variation(1).Reason(reason).Build();
            _testData.Update(_testData.Flag(flagKey).PreconfiguredFlag(flag));
            using (var client = MakeClient())
            {
                var expected = new EvaluationDetail<double>(2.5d, 1, reason);
                Assert.Equal(expected, client.DoubleVariationDetail(flagKey, 0.5d));
            }
        }

        [Fact]
        public void StringVariationReturnsValue()
        {
            _testData.Update(_testData.Flag(flagKey).Variation(LdValue.Of("string value")));
            using (var client = MakeClient())
            {
                Assert.Equal("string value", client.StringVariation(flagKey, ""));
            }
        }

        [Fact]
        public void StringVariationReturnsDefaultForUnknownFlag()
        {
            using (var client = MakeClient())
            {
                Assert.Equal("d", client.StringVariation(nonexistentFlagKey, "d"));
            }
        }

        [Fact]
        public void StringVariationDetailReturnsValue()
        {
            var reason = EvaluationReason.OffReason;
            var flag = new FeatureFlagBuilder().Value(LdValue.Of("string value")).Variation(1).Reason(reason).Build();
            _testData.Update(_testData.Flag(flagKey).PreconfiguredFlag(flag));
            using (var client = MakeClient())
            {
                var expected = new EvaluationDetail<string>("string value", 1, reason);
                Assert.Equal(expected, client.StringVariationDetail(flagKey, ""));
            }
        }

        [Fact]
        public void JsonVariationReturnsValue()
        {
            var jsonValue = LdValue.BuildObject().Add("thing", "stuff").Build();
            _testData.Update(_testData.Flag(flagKey).Variation(jsonValue));
            using (var client = MakeClient())
            {
                Assert.Equal(jsonValue, client.JsonVariation(flagKey, LdValue.Of(3)));
            }
        }

        [Fact]
        public void JsonVariationReturnsDefaultForUnknownFlag()
        {
            using (var client = MakeClient())
            {
                var defaultVal = LdValue.Of(3);
                Assert.Equal(defaultVal, client.JsonVariation(nonexistentFlagKey, defaultVal));
            }
        }

        [Fact]
        public void JsonVariationDetailReturnsValue()
        {
            var jsonValue = LdValue.BuildObject().Add("thing", "stuff").Build();
            var reason = EvaluationReason.OffReason;
            var flag = new FeatureFlagBuilder().Value(jsonValue).Variation(1).Reason(reason).Build();
            _testData.Update(_testData.Flag(flagKey).PreconfiguredFlag(flag));
            using (var client = MakeClient())
            {
                var expected = new EvaluationDetail<LdValue>(jsonValue, 1, reason);
                var result = client.JsonVariationDetail(flagKey, LdValue.Of(3));
                Assert.Equal(expected.Value, result.Value);
                Assert.Equal(expected.VariationIndex, result.VariationIndex);
                Assert.Equal(expected.Reason, result.Reason);
            }
        }

        [Fact]
        public void AllFlagsReturnsAllFlagValues()
        {
            _testData.Update(_testData.Flag("flag1").Variation(LdValue.Of("a")));
            _testData.Update(_testData.Flag("flag2").Variation(LdValue.Of("b")));
            using (var client = MakeClient())
            {
                var result = client.AllFlags();
                Assert.Equal(2, result.Count);
                Assert.Equal("a", result["flag1"].AsString);
                Assert.Equal("b", result["flag2"].AsString);
            }
        }
        
        [Fact]
        public void DefaultValueReturnedIfValueTypeIsDifferent()
        {
            _testData.Update(_testData.Flag(flagKey).Variation(LdValue.Of("string value")));
            using (var client = MakeClient())
            {
                Assert.Equal(3, client.IntVariation(flagKey, 3));
            }
        }

        [Fact]
        public void DefaultValueAndReasonIsReturnedIfValueTypeIsDifferent()
        {
            _testData.Update(_testData.Flag(flagKey).Variation(LdValue.Of("string value")));
            using (var client = MakeClient())
            {
                var expected = new EvaluationDetail<int>(3, null, EvaluationReason.ErrorReason(EvaluationErrorKind.WrongType));
                Assert.Equal(expected, client.IntVariationDetail(flagKey, 3));
            }
        }

        [Fact]
        public void DefaultValueReturnedIfFlagValueIsNull()
        {
            _testData.Update(_testData.Flag(flagKey).Variation(LdValue.Null));
            using (var client = MakeClient())
            {
                Assert.Equal(3, client.IntVariation(flagKey, 3));
            }
        }
    }
}
