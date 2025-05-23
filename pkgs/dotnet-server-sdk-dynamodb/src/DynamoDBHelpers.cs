using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    internal static class DynamoDBHelpers
    {
        /// <summary>
        /// Sends a list of write requests in batches as large as the AWS SDK will allow.
        /// </summary>
        /// <param name="client">the client</param>
        /// <param name="tableName">the table name</param>
        /// <param name="requests">list of requests</param>
        /// <returns>async Task with no return value</returns>
        public static async Task BatchWriteRequestsAsync(AmazonDynamoDBClient client, string tableName,
            List<WriteRequest> requests)
        {
            int batchSize = 25;
            for (int i = 0; i < requests.Count; i += batchSize)
            {
                var batch = requests.GetRange(i, Math.Min(batchSize, requests.Count - i));
                var request = new BatchWriteItemRequest(new Dictionary<string, List<WriteRequest>>()
                {
                    { tableName, batch }
                });
                await client.BatchWriteItemAsync(request);
            }
        }

        /// <summary>
        /// Executes a Query and continues to query any additional pages of results that are
        /// available, calling a (synchronous) Action for each individual result. The original
        /// QueryRequest will be modified.
        /// </summary>
        /// <param name="client">the client</param>
        /// <param name="request">the initial request</param>
        /// <param name="action">will be called for each result item</param>
        /// <returns>async Task with no return value</returns>
        public static async Task IterateQuery(AmazonDynamoDBClient client, QueryRequest request,
            Action<Dictionary<string, AttributeValue>> action)
        {
            while (true)
            {
                var resp = await client.QueryAsync(request);
                foreach (var item in resp.Items)
                {
                    action(item);
                }
                if (resp.LastEvaluatedKey == null || resp.LastEvaluatedKey.Count == 0)
                {
                    break;
                }
                request.ExclusiveStartKey = resp.LastEvaluatedKey;
            }
        }

        /// <summary>
        /// Executes a Scan and continues to query any additional pages of results that are
        /// available, calling a (synchronous) Action for each individual result. The original
        /// ScanRequest will be modified.
        /// </summary>
        /// <param name="client">the client</param>
        /// <param name="request">the initial request</param>
        /// <param name="action">will be called for each result item</param>
        /// <returns>async Task with no return value</returns>
        public static async Task IterateScan(AmazonDynamoDBClient client, ScanRequest request,
            Action<Dictionary<string, AttributeValue>> action)
        {
            while (true)
            {
                var resp = await client.ScanAsync(request);
                foreach (var item in resp.Items)
                {
                    action(item);
                }
                if (resp.LastEvaluatedKey == null || resp.LastEvaluatedKey.Count == 0)
                {
                    break;
                }
                request.ExclusiveStartKey = resp.LastEvaluatedKey;
            }
        }
    }
}
