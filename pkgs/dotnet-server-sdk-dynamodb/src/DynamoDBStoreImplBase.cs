using System;
using System.Collections.Generic;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using LaunchDarkly.Logging;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    internal abstract class DynamoDBStoreImplBase : IDisposable
    {
        protected readonly AmazonDynamoDBClient _client;
        protected readonly string _tableName;
        protected readonly string _prefix;
        protected readonly Logger _log;
        private readonly bool _wasExistingClient;

        protected DynamoDBStoreImplBase(
            AmazonDynamoDBClient client,
            bool wasExistingClient,
            string tableName,
            string prefix,
            Logger log
            )
        {
            _client = client;
            _wasExistingClient = wasExistingClient;
            _tableName = tableName;
            _log = log;

            if (string.IsNullOrEmpty(prefix))
            {
                _prefix = null;
                _log.Info("Using DynamoDB data store with table name \"{0}\" and no prefix", tableName);
            }
            else
            {
                _log.Info("Using DynamoDB data store with table name \"{0}\" and prefix \"{1}\"",
                    tableName, prefix);
                _prefix = prefix;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!_wasExistingClient)
                {
                    _client.Dispose();
                }
            }
        }

        protected string PrefixedNamespace(string baseStr) =>
            _prefix is null ? baseStr : (_prefix + ":" + baseStr);

        protected static Dictionary<string, AttributeValue> MakeKeysMap(string ns, string key) =>
            new Dictionary<string, AttributeValue>()
            {
                { DynamoDB.DataStorePartitionKey, new AttributeValue(ns) },
                { DynamoDB.DataStoreSortKey, new AttributeValue(key) }
            };
    }
}
