using System;
using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.Sdk.Client.Subsystems;
using Xunit;
using Xunit.Abstractions;

using static LaunchDarkly.Sdk.Client.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Client.Integrations
{
    public class TestDataTest : BaseTest
    {
        private static readonly Context _initialUser = Context.New("user0");
        private static readonly Context _initialContextWithKind1 = Context.New(ContextKind.Of("kind1"), "key1");
        private static readonly Context _initialContextWithKind2 = Context.New(ContextKind.Of("kind2"), "key1");

        private readonly TestData _td = TestData.DataSource();
        private readonly MockDataSourceUpdateSink _updates = new MockDataSourceUpdateSink();
        private readonly LdClientContext _context;

        public TestDataTest(ITestOutputHelper testOutput) : base(testOutput)
        {
            _context = new LdClientContext(Configuration.Builder("key", ConfigurationBuilder.AutoEnvAttributes.Disabled)
                .Logging(testLogging).Build(), _initialUser);
        }

        [Fact]
        public void InitializesWithEmptyData()
        {
            CreateAndStart();

            var initData = _updates.ExpectInit(_initialUser);
            Assert.Empty(initData.Items);
        }

        [Fact]
        public void InitializesWithFlags()
        {
            _td.Update(_td.Flag("flag1").Variation(true))
                .Update(_td.Flag("flag2").Variation(false));

            CreateAndStart();

            var initData = _updates.ExpectInit(_initialUser);
            var data = initData.Items.OrderBy(kv => kv.Key);
            Assert.Collection(data,
                FlagItemAssertion("flag1", 1, LdValue.Of(true), 0),
                FlagItemAssertion("flag2", 1, LdValue.Of(false), 1)
                );
        }

        [Fact]
        public void AddsFlag()
        {
            CreateAndStart();
            _updates.ExpectInit(_initialUser);

            _td.Update(_td.Flag("flag1").Variation(true));

            var item = _updates.ExpectUpsert(_initialUser, "flag1");
            VerifyUpdate(item, 1, LdValue.Of(true), 0);
        }

        [Fact]
        public void UpdatesFlag()
        {
            _td.Update(_td.Flag("flag1").Variation(true));

            CreateAndStart();
            _updates.ExpectInit(_initialUser);

            _td.Update(_td.Flag("flag1").Variation(false));

            var item = _updates.ExpectUpsert(_initialUser, "flag1");
            VerifyUpdate(item, 2, LdValue.Of(false), 1);
        }

        [Fact]
        public void FlagConfigBoolean()
        {
            var expectTrue = FlagValueAssertion(LdValue.Of(true), 0);
            var expectFalse = FlagValueAssertion(LdValue.Of(false), 1);

            VerifyFlag(f => f, expectTrue);
            VerifyFlag(f => f.BooleanFlag(), expectTrue);
            VerifyFlag(f => f.Variation(true), expectTrue);
            VerifyFlag(f => f.Variation(false), expectFalse);

            VerifyFlag(f => f.Variation(true).VariationForUser(_initialUser.Key, false), expectFalse);
            VerifyFlag(f => f.Variation(false).VariationForUser(_initialUser.Key, true), expectTrue);

            VerifyFlag(f => f.Variation(true).VariationForKey(_initialContextWithKind1.Kind,
                _initialContextWithKind1.Key, false), _initialContextWithKind1, expectFalse); // matched
            VerifyFlag(f => f.Variation(true).VariationForKey(_initialContextWithKind1.Kind,
                _initialContextWithKind1.Key, false), _initialContextWithKind2, expectTrue); // not matched
            VerifyFlag(f => f.Variation(false).VariationForKey(_initialContextWithKind1.Kind,
                _initialContextWithKind1.Key, true), _initialContextWithKind1, expectTrue); // matched
            VerifyFlag(f => f.Variation(false).VariationForKey(_initialContextWithKind1.Kind,
                _initialContextWithKind1.Key, true), _initialContextWithKind2, expectFalse); // not matched

            VerifyFlag(f => f.Variation(true).VariationFunc(u => false), expectFalse);
            VerifyFlag(f => f.Variation(false).VariationFunc(u => true), expectTrue);

            // VariationForUser takes precedence over VariationFunc
            VerifyFlag(f => f.Variation(true).VariationForUser(_initialUser.Key, false)
                .VariationFunc(u => true), expectFalse);
            VerifyFlag(f => f.Variation(false).VariationForUser(_initialUser.Key, true)
                .VariationFunc(u => false), expectTrue);
        }

        [Fact]
        public void FlagConfigByVariationIndex()
        {
            LdValue aVal = LdValue.Of("a"), bVal = LdValue.Of("b");
            int aIndex = 0, bIndex = 1;
            var ab = new LdValue[] { aVal, bVal };
            var expectA = FlagValueAssertion(LdValue.Of("a"), aIndex);
            var expectB = FlagValueAssertion(LdValue.Of("b"), bIndex);

            VerifyFlag(f => f.Variations(ab), expectA);
            VerifyFlag(f => f.Variations(ab).Variation(aIndex), expectA);
            VerifyFlag(f => f.Variations(ab).Variation(bIndex), expectB);

            VerifyFlag(f => f.Variations(ab).Variation(aIndex).VariationForUser(_initialUser.Key, bIndex), expectB);
            VerifyFlag(f => f.Variations(ab).Variation(bIndex).VariationForUser(_initialUser.Key, aIndex), expectA);

            VerifyFlag(f => f.Variations(ab).Variation(aIndex).VariationForKey(_initialContextWithKind1.Kind,
                _initialContextWithKind1.Key, bIndex), _initialContextWithKind1, expectB); // matched
            VerifyFlag(f => f.Variations(ab).Variation(aIndex).VariationForKey(_initialContextWithKind1.Kind,
                _initialContextWithKind1.Key, bIndex), _initialContextWithKind2, expectA); // not matched

            VerifyFlag(f => f.Variations(ab).Variation(aIndex).VariationFunc(u => bIndex), expectB);
            VerifyFlag(f => f.Variations(ab).Variation(bIndex).VariationFunc(u => aIndex), expectA);

            // VariationForUser takes precedence over VariationFunc
            VerifyFlag(f => f.Variations(ab).Variation(aIndex).VariationForUser(_initialUser.Key, bIndex)
                .VariationFunc(u => aIndex), expectB);
            VerifyFlag(f => f.Variations(ab).Variation(bIndex).VariationForUser(_initialUser.Key, aIndex)
                .VariationFunc(u => bIndex), expectA);
        }

        [Fact]
        public void FlagConfigByValue()
        {
            LdValue aVal = LdValue.Of("a"), bVal = LdValue.Of("b");
            int aIndex = 0, bIndex = 1;
            var ab = new LdValue[] { aVal, bVal };
            var expectA = FlagValueAssertion(LdValue.Of("a"), aIndex);
            var expectB = FlagValueAssertion(LdValue.Of("b"), bIndex);

            VerifyFlag(f => f.Variations(ab).Variation(aVal), expectA);
            VerifyFlag(f => f.Variations(ab).Variation(bVal), expectB);

            VerifyFlag(f => f.Variations(ab).Variation(aVal).VariationForUser(_initialUser.Key, bVal), expectB);
            VerifyFlag(f => f.Variations(ab).Variation(bVal).VariationForUser(_initialUser.Key, aVal), expectA);

            VerifyFlag(f => f.Variations(ab).Variation(aVal).VariationForKey(_initialContextWithKind1.Kind,
                _initialContextWithKind1.Key, bVal), _initialContextWithKind1, expectB); // matched
            VerifyFlag(f => f.Variations(ab).Variation(aVal).VariationForKey(_initialContextWithKind1.Kind,
                _initialContextWithKind1.Key, bVal), _initialContextWithKind2, expectA); // not matched

            VerifyFlag(f => f.Variations(ab).Variation(aVal).VariationFunc(u => bVal), expectB);
            VerifyFlag(f => f.Variations(ab).Variation(bVal).VariationFunc(u => aVal), expectA);

            // VariationForUser takes precedence over VariationFunc
            VerifyFlag(f => f.Variations(ab).Variation(aVal).VariationForUser(_initialUser.Key, bVal)
                .VariationFunc(u => aVal), expectB);
            VerifyFlag(f => f.Variations(ab).Variation(bVal).VariationForUser(_initialUser.Key, aVal)
                .VariationFunc(u => bVal), expectA);
        }

        [Fact]
        public void UsePreconfiguredFlag()
        {
            CreateAndStart();
            _updates.ExpectInit(_initialUser);

            var flag = new FeatureFlagBuilder().Version(1).Value(true).Variation(0).Reason(EvaluationReason.OffReason)
                .TrackEvents(true).TrackReason(true).DebugEventsUntilDate(UnixMillisecondTime.OfMillis(123)).Build();
            _td.Update(_td.Flag("flag1").PreconfiguredFlag(flag));

            var item1 = _updates.ExpectUpsert(_initialUser, "flag1");
            Assert.Equal(flag, item1.Item);

            _td.Update(_td.Flag("flag1").PreconfiguredFlag(flag));

            var item2 = _updates.ExpectUpsert(_initialUser, "flag1");
            var updatedFlag = new FeatureFlagBuilder(flag).Version(2).Build();
            Assert.Equal(updatedFlag, item2.Item);
        }

        private void CreateAndStart()
        {
            var ds = _td.Build(_context.WithDataSourceUpdateSink(_updates).WithContextAndBackgroundState(_initialUser, false));
            var started = ds.Start();
            Assert.True(started.IsCompleted);
        }

        private Action<KeyValuePair<string, ItemDescriptor>> FlagItemAssertion(
            string key,
            int version,
            LdValue value,
            int? variation
            )
        {
            return kv =>
            {
                Assert.Equal(key, kv.Key);
                VerifyUpdate(kv.Value, version, value, variation);
            };
        }

        private Action<ItemDescriptor> FlagValueAssertion(
            LdValue value,
            int? variation
            )
        {
            return item =>
            {
                Assert.Equal(value, item.Item.Value);
                Assert.Equal(variation, item.Item.Variation);
            };
        }

        private void VerifyUpdate(ItemDescriptor item, int version, LdValue value, int? variation)
        {
            Assert.Equal(version, item.Version);
            Assert.Equal(value, item.Item.Value);
            Assert.Equal(variation, item.Item.Variation);
        }

        private void VerifyFlag(Func<TestData.FlagBuilder, TestData.FlagBuilder> builderFn,
            Context context,
            Action<ItemDescriptor> assertion)
        {
            var tdTemp = TestData.DataSource();
            using (var ds = tdTemp.Build(_context.WithDataSourceUpdateSink(_updates).WithContextAndBackgroundState(context, false)))
            {
                ds.Start();
                _updates.ExpectInit(context);
                tdTemp.Update(builderFn(tdTemp.Flag("flag")));
                var up = _updates.ExpectUpsert(context, "flag");
                assertion(up);
            }
        }

        private void VerifyFlag(Func<TestData.FlagBuilder, TestData.FlagBuilder> builderFn,
            Action<ItemDescriptor> assertion) =>
            VerifyFlag(builderFn, _initialUser, assertion);
    }
}
