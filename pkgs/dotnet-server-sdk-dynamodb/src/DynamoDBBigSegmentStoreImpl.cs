using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Subsystems;

using static LaunchDarkly.Sdk.Server.Subsystems.BigSegmentStoreTypes;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    internal sealed class DynamoDBBigSegmentStoreImpl : DynamoDBStoreImplBase, IBigSegmentStore
    {
        internal const string MembershipKey = "big_segments_user";
        internal const string IncludedAttr = "included";
        internal const string ExcludedAttr = "excluded";

        internal const string MetadataKey = "big_segments_metadata";
        internal const string SyncTimeAttr = "synchronizedOn";

        internal DynamoDBBigSegmentStoreImpl(
            AmazonDynamoDBClient client,
            bool wasExistingClient,
            string tableName,
            string prefix,
            Logger log
            ) : base(client, wasExistingClient, tableName, prefix, log)
        { }

        public async Task<IMembership> GetMembershipAsync(string userHash)
        {
            var namespaceKey = PrefixedNamespace(MembershipKey);
            var request = new GetItemRequest(_tableName, MakeKeysMap(namespaceKey, userHash), true);
            var result = await _client.GetItemAsync(request);
            if (result.Item is null || result.Item.Count == 0)
            {
                return null;
            }
            var includedRefs = GetStringListFromSetAttr(result.Item, IncludedAttr);
            var excludedRefs = GetStringListFromSetAttr(result.Item, ExcludedAttr);
            return NewMembershipFromSegmentRefs(includedRefs, excludedRefs);
        }

        private static IEnumerable<string> GetStringListFromSetAttr(
            Dictionary<string, AttributeValue> attrs,
            string attrName
            ) =>
            attrs.TryGetValue(attrName, out var attr) ? attr.SS : null;

        public async Task<StoreMetadata?> GetMetadataAsync()
        {
            var key = PrefixedNamespace(MetadataKey);
            var request = new GetItemRequest(_tableName, MakeKeysMap(key, key), true);
            var result = await _client.GetItemAsync(request);
            if (result.Item is null || result.Item.Count == 0)
            {
                return null;
            }
            if (!result.Item.TryGetValue(SyncTimeAttr, out var syncTimeValue) || string.IsNullOrEmpty(syncTimeValue.N))
            {
                return new StoreMetadata { LastUpToDate = null };
            }
            if (!long.TryParse(syncTimeValue.N, out var milliseconds))
            {
                throw new InvalidOperationException("Invalid data in DynamoDB: non-numeric timestamp");
            }
            return new StoreMetadata { LastUpToDate = UnixMillisecondTime.OfMillis(milliseconds) };
        }
    }
}
