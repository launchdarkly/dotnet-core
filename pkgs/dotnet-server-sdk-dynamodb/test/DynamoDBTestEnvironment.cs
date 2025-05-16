using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    public static class DynamoDBTestEnvironment
    {
        public const string TableName = "test-dynamodb-table";

        private static bool TableCreated;
        private static SemaphoreSlim _tableLock = new SemaphoreSlim(1, 1);

        public static AWSCredentials MakeTestCredentials() =>
            new BasicAWSCredentials("key", "secret"); // not used, but required

        public static AmazonDynamoDBConfig MakeTestConfiguration() =>
            new AmazonDynamoDBConfig()
            {
                ServiceURL = "http://localhost:8000" // assumes we're running a local DynamoDB
            };

        public static AmazonDynamoDBClient client = CreateTestClient();

        public static async Task CreateTableIfNecessary()
        {
            await _tableLock.WaitAsync();
            try
            {
                if (TableCreated)
                {
                    return;
                }

                try
                {
                    await client.DescribeTableAsync(new DescribeTableRequest(TableName));
                    return; // table exists
                }
                catch (ResourceNotFoundException)
                {
                    // fall through to code below - we'll create the table
                }

                var request = new CreateTableRequest()
                {
                    TableName = TableName,
                    KeySchema = new List<KeySchemaElement>()
                    {
                        new KeySchemaElement(DynamoDB.DataStorePartitionKey, KeyType.HASH),
                        new KeySchemaElement(DynamoDB.DataStoreSortKey, KeyType.RANGE)
                    },
                    AttributeDefinitions = new List<AttributeDefinition>()
                    {
                        new AttributeDefinition(DynamoDB.DataStorePartitionKey, ScalarAttributeType.S),
                        new AttributeDefinition(DynamoDB.DataStoreSortKey, ScalarAttributeType.S)
                    },
                    ProvisionedThroughput = new ProvisionedThroughput(1, 1)
                };
                await client.CreateTableAsync(request);
            }
            finally
            {
                TableCreated = true;
                _tableLock.Release();
            }
        }

        public static async Task ClearAllData(string prefix)
        {
            var keyPrefix = prefix is null ? "" : (prefix + ":");

            var deleteReqs = new List<WriteRequest>();
            ScanRequest request = new ScanRequest(TableName)
            {
                ConsistentRead = true,
                ProjectionExpression = "#namespace, #key",
                ExpressionAttributeNames = new Dictionary<string, string>()
                {
                    { "#namespace", DynamoDB.DataStorePartitionKey },
                    { "#key", DynamoDB.DataStoreSortKey }
                }
            };
            await DynamoDBHelpers.IterateScan(client, request,
                item =>
                {
                    if (item[DynamoDB.DataStorePartitionKey].S.StartsWith(keyPrefix))
                    {
                        deleteReqs.Add(new WriteRequest(new DeleteRequest(item)));
                    }
                });
            await DynamoDBHelpers.BatchWriteRequestsAsync(client, TableName, deleteReqs);
        }

        public static AmazonDynamoDBClient CreateTestClient() =>
            new AmazonDynamoDBClient(MakeTestCredentials(), MakeTestConfiguration());
    }
}
