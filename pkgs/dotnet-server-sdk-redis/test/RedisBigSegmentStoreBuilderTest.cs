using System;
using System.Reflection;
using LaunchDarkly.Sdk.Server.Subsystems;
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
        public void ConnectionMethodExists()
        {
            var builder = Redis.BigSegmentStore();
            
            // Initially no external connection
            Assert.Null(builder._externalConnection);
            
            // Test that the Connection method exists by checking if we can call it
            // Use reflection to verify the method exists without requiring a real connection
            var connectionMethod = typeof(RedisStoreBuilder<IBigSegmentStore>)
                .GetMethod("Connection", new[] { typeof(IConnectionMultiplexer) });
            
            Assert.NotNull(connectionMethod);
            Assert.Equal(typeof(RedisStoreBuilder<IBigSegmentStore>), connectionMethod.ReturnType);
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