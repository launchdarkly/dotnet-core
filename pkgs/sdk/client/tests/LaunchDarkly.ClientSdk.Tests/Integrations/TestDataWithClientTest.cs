using System;
using Xunit;
using Xunit.Abstractions;

namespace LaunchDarkly.Sdk.Client.Integrations
{
    public class TestDataWithClientTest : BaseTest
    {
        private readonly TestData _td = TestData.DataSource();
        private readonly Configuration _config;
        private readonly Context _user = Context.New("userkey");

        public TestDataWithClientTest(ITestOutputHelper testOutput) : base(testOutput)
        {
            _config = Configuration.Builder("mobile-key", ConfigurationBuilder.AutoEnvAttributes.Disabled)
                .DataSource(_td)
                .Events(Components.NoEvents)
                .Build();
        }

        [Fact]
        public void InitializesWithEmptyData()
        {
            using (var client = LdClient.Init(_config, _user, TimeSpan.FromSeconds(1)))
            {
                Assert.True(client.Initialized);
            }
        }

        [Fact]
        public void InitializesWithFlag()
        {
            _td.Update(_td.Flag("flag").Variation(true));

            using (var client = LdClient.Init(_config, _user, TimeSpan.FromSeconds(1)))
            {
                Assert.True(client.BoolVariation("flag", false));
            }
        }

        [Fact]
        public void UpdatesFlag()
        {
            using (var client = LdClient.Init(_config, _user, TimeSpan.FromSeconds(1)))
            {
                Assert.False(client.BoolVariation("flag", false));

                _td.Update(_td.Flag("flag").Variation(true));

                Assert.True(client.BoolVariation("flag", false));
            }
        }

        [Fact]
        public void CanSetValuePerUser()
        {
            _td.Update(_td.Flag("flag")
                .Variations(LdValue.Of("red"), LdValue.Of("green"), LdValue.Of("blue"))
                .Variation(LdValue.Of("red"))
                .VariationForUser("user1", LdValue.Of("green"))
                .VariationForUser("user2", LdValue.Of("blue"))
                .VariationFunc(user =>
                    user.GetValue("favoriteColor")
                ));
            var user1 = Context.New("user1");
            var user2 = Context.New("user2");
            var user3 = Context.Builder("user3").Set("favoriteColor", "green").Build();

            using (var client = LdClient.Init(_config, user1, TimeSpan.FromSeconds(1)))
            {
                Assert.Equal("green", client.StringVariation("flag", ""));

                client.Identify(user2, TimeSpan.FromSeconds(1));

                Assert.Equal("blue", client.StringVariation("flag", ""));

                client.Identify(user3, TimeSpan.FromSeconds(1));

                Assert.Equal("green", client.StringVariation("flag", ""));
            }
        }

        [Fact]
        public void CanSetValuePerContext()
        {
            ContextKind kind1 = ContextKind.Of("kind1"), kind2 = ContextKind.Of("kind2");
            _td.Update(_td.Flag("flag")
                .Variations(LdValue.Of("red"), LdValue.Of("green"), LdValue.Of("blue"))
                .Variation(LdValue.Of("red"))
                .VariationForKey(kind1, "key1", LdValue.Of("green"))
                .VariationForKey(kind1, "key2", LdValue.Of("blue"))
                .VariationForKey(kind2, "key1", LdValue.Of("blue"))
                .VariationFunc(context =>
                    context.GetValue("favoriteColor")
                ));
            var context1 = Context.New(kind1, "key1");
            var context2 = Context.New(kind1, "key2");
            var context3 = Context.New(kind2, "key1");
            var context4 = Context.Builder("key4").Set("favoriteColor", "green").Build();

            using (var client = LdClient.Init(_config, context1, TimeSpan.FromSeconds(1)))
            {
                Assert.Equal("green", client.StringVariation("flag", ""));

                client.Identify(context2, TimeSpan.FromSeconds(1));
                Assert.Equal("blue", client.StringVariation("flag", ""));

                client.Identify(context3, TimeSpan.FromSeconds(1));
                Assert.Equal("blue", client.StringVariation("flag", ""));

                client.Identify(context4, TimeSpan.FromSeconds(1));
                Assert.Equal("green", client.StringVariation("flag", ""));
            }
        }
    }
}
