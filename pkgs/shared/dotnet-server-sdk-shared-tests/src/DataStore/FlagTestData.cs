using System.Collections.Generic;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.SharedTests.DataStore
{
    public static class FlagTestData
    {
        public const string FlagKey = "flagkey", SegmentKey = "segmentkey",
            UserKey = "userkey", OtherUserKey = "otheruser";

        public static readonly LdValue GoodValue1 = LdValue.Of("good"),
            GoodValue2 = LdValue.Of("better"), BadValue = LdValue.Of("bad");

        public const int GoodVariation1 = 0, GoodVariation2 = 1, BadVariation = 2;

        public static readonly Context MainUser = Context.New(UserKey),
            OtherUser = Context.New(OtherUserKey);

        public static ItemDescriptor MakeFlagThatReturnsVariationForSegmentMatch(int version, int variation)
        {
            var flagJson = LdValue.BuildObject()
                .Add("key", FlagKey)
                .Add("version", version)
                .Add("on", true)
                .Add("variations", LdValue.ArrayOf(GoodValue1, GoodValue2, BadValue))
                .Add("fallthrough", LdValue.BuildObject().Add("variation", BadVariation).Build())
                .Add("rules", LdValue.BuildArray()
                    .Add(LdValue.BuildObject()
                        .Add("variation", variation)
                        .Add("clauses", LdValue.BuildArray()
                            .Add(LdValue.BuildObject()
                                .Add("attribute", "")
                                .Add("op", "segmentMatch")
                                .Add("values", LdValue.ArrayOf(LdValue.Of(SegmentKey)))
                                .Build())
                            .Build())
                        .Build())
                    .Build())
                .Build().ToJsonString();
            return DataModel.Features.Deserialize(flagJson);
        }

        public static ItemDescriptor MakeSegmentThatMatchesUserKeys(int version, params string[] keys)
        {
            var segmentJson = LdValue.BuildObject()
                .Add("key", SegmentKey)
                .Add("version", version)
                .Add("included", LdValue.Convert.String.ArrayFrom(keys))
                .Build().ToJsonString();
            return DataModel.Segments.Deserialize(segmentJson);
        }

        public static FullDataSet<ItemDescriptor> MakeFullDataSet(ItemDescriptor flag, ItemDescriptor segment)
        {
            return new FullDataSet<ItemDescriptor>(
                new Dictionary<DataKind, KeyedItems<ItemDescriptor>>
                {
                    {
                        DataModel.Features,
                        new KeyedItems<ItemDescriptor>(
                            new Dictionary<string, ItemDescriptor>
                            { { FlagKey, flag } }
                        )
                    },
                    {
                        DataModel.Segments,
                        new KeyedItems<ItemDescriptor>(
                            new Dictionary<string, ItemDescriptor>
                            { { SegmentKey, segment } }
                        )
                    },
                }
            );
        }
    }
}
