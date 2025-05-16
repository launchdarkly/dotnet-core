using System.Collections.Generic;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using LaunchDarkly.Sdk.Server.Subsystems;
using LaunchDarkly.Sdk.Server.SharedTests.BigSegmentStore;
using Xunit;
using Xunit.Abstractions;
using static LaunchDarkly.Sdk.Server.Integrations.DynamoDBTestEnvironment;
using static LaunchDarkly.Sdk.Server.Subsystems.BigSegmentStoreTypes;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    public class DynamoDBBigSegmentStoreTest : BigSegmentStoreBaseTests, IAsyncLifetime
    {
        override protected BigSegmentStoreTestConfig Configuration => new BigSegmentStoreTestConfig
        {
            StoreFactoryFunc = MakeStoreFactory,
            ClearDataAction = ClearAllData,
            SetMetadataAction = SetMetadata,
            SetSegmentsAction = SetSegments
        };

        public DynamoDBBigSegmentStoreTest(ITestOutputHelper testOutput) : base(testOutput)
        {
        }

        public Task InitializeAsync() => CreateTableIfNecessary();

        public Task DisposeAsync() => Task.CompletedTask;

        private IComponentConfigurer<IBigSegmentStore> MakeStoreFactory(string prefix) =>
            DynamoDB.BigSegmentStore(TableName)
                .ExistingClient(DynamoDBTestEnvironment.client)
                .Prefix(prefix);

        private async Task SetMetadata(string prefix, StoreMetadata metadata)
        {
            var client = DynamoDBTestEnvironment.client;
            var key = prefix + ":" + DynamoDBBigSegmentStoreImpl.MetadataKey;
            var timeValue = metadata.LastUpToDate.HasValue ? metadata.LastUpToDate.Value.Value.ToString() : null;
            await client.PutItemAsync(new PutItemRequest(TableName,
                new Dictionary<string, AttributeValue>
                {
                    { DynamoDB.DataStorePartitionKey, new AttributeValue { S = key } },
                    { DynamoDB.DataStoreSortKey, new AttributeValue { S = key } },
                    { DynamoDBBigSegmentStoreImpl.SyncTimeAttr, new AttributeValue { N = timeValue } }
                }));
        }

        private async Task SetSegments(string prefix, string userHash,
            IEnumerable<string> includedRefs, IEnumerable<string> excludedRefs)
        {
            var client = DynamoDBTestEnvironment.client;
            if (includedRefs != null)
            {
                foreach (var value in includedRefs)
                {
                    await AddToSetAsync(client, prefix, userHash, DynamoDBBigSegmentStoreImpl.IncludedAttr, value);
                }
            }

            if (excludedRefs != null)
            {
                foreach (var value in excludedRefs)
                {
                    await AddToSetAsync(client, prefix, userHash, DynamoDBBigSegmentStoreImpl.ExcludedAttr, value);
                }
            }
        }

        private async Task AddToSetAsync(AmazonDynamoDBClient client, string prefix,
            string userHash, string attrName, string value)
        {
            var namespaceKey = prefix + ":" + DynamoDBBigSegmentStoreImpl.MembershipKey;
            await client.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    { DynamoDB.DataStorePartitionKey, new AttributeValue { S = namespaceKey } },
                    { DynamoDB.DataStoreSortKey, new AttributeValue { S = userHash } },
                },
                UpdateExpression = string.Format("ADD {0} :value", attrName),
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    { ":value", new AttributeValue { SS = new List<string> { value } } }
                }
            });
        }
    }
}
