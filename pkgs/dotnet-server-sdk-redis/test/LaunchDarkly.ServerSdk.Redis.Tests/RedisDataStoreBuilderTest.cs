using System;
using System.Collections.Generic;
using System.Net;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    public class RedisDataStoreBuilderTest
    {
        [Fact]
        public void DefaultConfigHasDefaultRedisHostAndPort()
        {
            var builder = Redis.DataStore();
            Assert.Collection(builder._redisConfig.EndPoints,
                e => Assert.Equal(new DnsEndPoint("localhost", 6379), e));
        }

        [Fact]
        public void EndPoint()
        {
            var builder = Redis.DataStore();
            DnsEndPoint ep = new DnsEndPoint("test", 9999);
            builder.EndPoint(ep);
            Assert.Collection(builder._redisConfig.EndPoints,
                e => Assert.Equal(ep, e));
        }

        [Fact]
        public void MultipleEndPoints()
        {
            var builder = Redis.DataStore();
            DnsEndPoint ep1 = new DnsEndPoint("test", 9998);
            DnsEndPoint ep2 = new DnsEndPoint("test", 9999);
            builder.EndPoints(new List<EndPoint> { ep1, ep2 });
            Assert.Collection(builder._redisConfig.EndPoints,
                e => Assert.Equal(ep1, e),
                e => Assert.Equal(ep2, e));
        }

        [Fact]
        public void HostAndPort()
        {
            var builder = Redis.DataStore();
            builder.HostAndPort("test", 9999);
            Assert.Collection(builder._redisConfig.EndPoints,
                e => Assert.Equal(new DnsEndPoint("test", 9999), e));
        }

        [Fact]
        public void MinimalUri()
        {
            var builder = Redis.DataStore();
            builder.Uri(new Uri("redis://test:9999"));
            Assert.Collection(builder._redisConfig.EndPoints,
                e => Assert.Equal(new DnsEndPoint("test", 9999), e));
            Assert.Null(builder._redisConfig.Password);
            Assert.Null(builder._redisConfig.DefaultDatabase);
        }

        [Fact]
        public void UriWithPassword()
        {
            var builder = Redis.DataStore();
            builder.Uri(new Uri("redis://:secret@test:9999"));
            Assert.Collection(builder._redisConfig.EndPoints,
                e => Assert.Equal(new DnsEndPoint("test", 9999), e));
            Assert.Equal("secret", builder._redisConfig.Password);
            Assert.Null(builder._redisConfig.DefaultDatabase);
        }

        [Fact]
        public void UriWithDatabase()
        {
            var builder = Redis.DataStore();
            builder.Uri(new Uri("redis://@test:9999/8"));
            Assert.Collection(builder._redisConfig.EndPoints,
                e => Assert.Equal(new DnsEndPoint("test", 9999), e));
            Assert.Null(builder._redisConfig.Password);
            Assert.Equal(8, builder._redisConfig.DefaultDatabase);
        }

        [Fact]
        public void Database()
        {
            var builder = Redis.DataStore();
            builder.DatabaseIndex(8);
            Assert.Equal(8, builder._redisConfig.DefaultDatabase);
        }

        [Fact]
        public void ConnectTimeout()
        {
            var builder = Redis.DataStore();
            builder.ConnectTimeout(TimeSpan.FromSeconds(8));
            Assert.Equal(8000, builder._redisConfig.ConnectTimeout);
        }

        [Fact]
        public void OperationTimeout()
        {
            var builder = Redis.DataStore();
            builder.OperationTimeout(TimeSpan.FromSeconds(8));
            Assert.Equal(8000, builder._redisConfig.SyncTimeout);
        }

        [Fact]
        public void Prefix()
        {
            var builder = Redis.DataStore();
            Assert.Equal(Redis.DefaultPrefix, builder._prefix);
            builder.Prefix("abc");
            Assert.Equal("abc", builder._prefix);
            builder.Prefix(null);
            Assert.Equal(Redis.DefaultPrefix, builder._prefix);
        }
    }
}
