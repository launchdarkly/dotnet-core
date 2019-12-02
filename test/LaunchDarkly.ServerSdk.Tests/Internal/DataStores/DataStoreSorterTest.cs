﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LaunchDarkly.Sdk.Server.Internal.Model;
using Xunit;

using static LaunchDarkly.Sdk.Server.Interfaces.DataStoreTypes;
using static LaunchDarkly.Sdk.Server.Internal.DataStores.DataStoreTestTypes;

namespace LaunchDarkly.Sdk.Server.Internal.DataStores
{
    public class DataStoreSorterTest
    {
        [Fact]
        public void PrerequisiteFlagsAreUpdatedBeforeFlagsThatUseThem()
        {
            var sortedData = DataStoreSorter.SortAllCollections(DependencyOrderingTestData);
            VerifyDataSetOrder(sortedData, DependencyOrderingTestData, ExpectedOrderingForSortedDataSet);
        }

        [Fact]
        public void PrerequisiteFlagsAreUpdatedBeforeFlagsThatUseThemWhenInputDataIsReversed()
        {
            var inputDataWithReverseOrder = new FullDataSet<ItemDescriptor>(DependencyOrderingTestData.Data.Reverse().Select(kv =>
                new KeyValuePair<DataKind, IEnumerable<KeyValuePair<string, ItemDescriptor>>>(kv.Key, kv.Value.Reverse())
            ));
            var sortedData = DataStoreSorter.SortAllCollections(inputDataWithReverseOrder);
            VerifyDataSetOrder(sortedData, DependencyOrderingTestData, ExpectedOrderingForSortedDataSet);
        }

        internal static readonly FullDataSet<ItemDescriptor> DependencyOrderingTestData =
            new TestDataBuilder()
                .Add(DataKinds.Features, "a", 1,
                    new FeatureFlagBuilder("a")
                        .Prerequisites(new List<Prerequisite>()
                        {
                            new Prerequisite("b", 0),
                            new Prerequisite("c", 0),
                        })
                    .Build())
                .Add(DataKinds.Features, "b", 1,
                    new FeatureFlagBuilder("b")
                        .Prerequisites(new List<Prerequisite>()
                        {
                            new Prerequisite("c", 0),
                            new Prerequisite("e", 0),
                        })
                    .Build())
                .Add(DataKinds.Features, "c", 1, new FeatureFlagBuilder("c").Build())
                .Add(DataKinds.Features, "d", 1, new FeatureFlagBuilder("d").Build())
                .Add(DataKinds.Features, "e", 1, new FeatureFlagBuilder("e").Build())
                .Add(DataKinds.Features, "f", 1, new FeatureFlagBuilder("f").Build())
                .Add(DataKinds.Segments, "o", 1, new Segment("o", 1, null, null, null, null, false))
                .Build();

        internal struct KeyOrderConstraint
        {
            internal string EarlierKey { get; set; }
            internal string LaterKey { get; set; }
        }

        internal static List<KeyValuePair<DataKind, List<KeyOrderConstraint>>> ExpectedOrderingForSortedDataSet =
            new List<KeyValuePair<DataKind, List<KeyOrderConstraint>>>()
                {
                    new KeyValuePair<DataKind, List<KeyOrderConstraint>>(DataKinds.Segments,
                        new List<KeyOrderConstraint>()), // ordering within segments doesn't matter
                    new KeyValuePair<DataKind, List<KeyOrderConstraint>>(DataKinds.Features,
                        new List<KeyOrderConstraint>()
                        {
                            new KeyOrderConstraint { EarlierKey = "c", LaterKey = "b" },
                            new KeyOrderConstraint { EarlierKey = "e", LaterKey = "b" },
                            new KeyOrderConstraint { EarlierKey = "b", LaterKey = "a" },
                            new KeyOrderConstraint { EarlierKey = "c", LaterKey = "a" }
                        }
                    ),
                };
        
        internal static void VerifyDataSetOrder(FullDataSet<ItemDescriptor> resultData, FullDataSet<ItemDescriptor> inputData,
            List<KeyValuePair<DataKind, List<KeyOrderConstraint>>> expectedOrdering)
        {
            // Verify that there are the right number of data kinds in the right order
            Assert.Equal(expectedOrdering.Select(kv => kv.Key).ToList(),
                resultData.Data.Select(kv => kv.Key).ToList());

            foreach (var kindAndItems in resultData.Data)
            {
                var inputItemsForKind = inputData.Data.First(kv => kv.Key == kindAndItems.Key).Value;

                // Verify that all of the input items are present, regardless of order
                Assert.Equal(new HashSet<KeyValuePair<string, ItemDescriptor>>(kindAndItems.Value),
                    new HashSet<KeyValuePair<string, ItemDescriptor>>(inputItemsForKind));

                // Verify that for any two keys where we care about their relative ordering, they are in the right order
                // (keys where the relative ordering doesn't matter, i.e. there are no prerequisites involved, are simply
                // omitted from the constraint list)
                var constraints = expectedOrdering.Where(kv => kv.Key == kindAndItems.Key).Select(kv => kv.Value).FirstOrDefault();
                var resultKeys = kindAndItems.Value.Select(kv => kv.Key).ToList();
                foreach (var constraint in constraints ?? new List<KeyOrderConstraint>())
                {
                    int indexOfEarlierKey = resultKeys.IndexOf(constraint.EarlierKey);
                    int indexOfLaterKey = resultKeys.IndexOf(constraint.LaterKey);
                    Assert.True(indexOfEarlierKey < indexOfLaterKey,
                        String.Format("In \"{0}\", \"{1}\" should be updated before \"{2}\"; actual key order was {3}",
                            kindAndItems.Key.Name, constraint.EarlierKey, constraint.LaterKey, resultKeys));
                }
            }
        }
    }
}
