using System;
using StackExchange.Redis;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    public class RedisBigSegmentStoreBuilderTest
    {
        [Fact]
        public void ConnectionWithNull()
        {
            var builder = Redis.BigSegmentStore();

            // Setting null connection should throw ArgumentNullException
            Assert.Throws<ArgumentNullException>(() => builder.Connection(null));
        }

        [Fact]
        public void ConnectionMethodSetsExternalConnection()
        {
            var builder = Redis.BigSegmentStore();
            var connection = ConnectionMultiplexer.Connect(new ConfigurationOptions()
            {
                EndPoints = { "localhost:6379" }
            });

            // Initially no external connection
            Assert.Null(builder._externalConnection);

            // Set the connection
            builder.Connection(connection);

            // Verify the connection was set
            Assert.Same(connection, builder._externalConnection);
        }

        [Fact]
        public void ConnectionWorksWithOtherBuilderMethods()
        {
            var builder = Redis.BigSegmentStore();

            // Chain with other builder methods
            builder.Prefix("test-prefix");

            Assert.Equal("test-prefix", builder._prefix);
        }
    }
}
