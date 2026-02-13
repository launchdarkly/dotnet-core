using LaunchDarkly.Sdk.Server.Interfaces;
using Xunit;
using Xunit.Abstractions;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    public class TestDataWithClientTest : BaseTest
    {
        private readonly TestData _td = TestData.DataSource();
        private readonly Configuration _config;
        private readonly Context _user = Context.New("userkey");

        public TestDataWithClientTest(ITestOutputHelper testOutput) : base(testOutput)
        {
            _config = BasicConfig()
                .DataSource(_td)
                .Events(Components.NoEvents)
                .Build();
        }

        [Fact]
        public void InitializesWithEmptyData()
        {
            using (var client = new LdClient(_config))
            {
                Assert.True(client.Initialized);
            }
        }

        [Fact]
        public void InitializesWithFlag()
        {
            _td.Update(_td.Flag("flag").On(true));

            using (var client = new LdClient(_config))
            {
                Assert.True(client.BoolVariation("flag", _user, false));
            }
        }

        [Fact]
        public void UpdatesFlag()
        {
            using (var client = new LdClient(_config))
            {
                Assert.False(client.BoolVariation("flag", _user, false));

                _td.Update(_td.Flag("flag").On(true));

                Assert.True(client.BoolVariation("flag", _user, false));
            }
        }

        [Fact]
        public void UsesTargets()
        {
            _td.Update(_td.Flag("flag").FallthroughVariation(false).VariationForUser("user1", true));

            using (var client = new LdClient(_config))
            {
                Assert.True(client.BoolVariation("flag", Context.New("user1"), false));
                Assert.False(client.BoolVariation("flag", Context.New("user2"), false));
            }
        }

        [Fact]
        public void UsesRules()
        {
            _td.Update(_td.Flag("flag").FallthroughVariation(false)
                .IfMatch("name", LdValue.Of("Lucy")).ThenReturn(true)
                .IfMatch("name", LdValue.Of("Mina")).ThenReturn(true));

            using (var client = new LdClient(_config))
            {
                Assert.True(client.BoolVariation("flag", Context.Builder("user1").Name("Lucy").Build(), false));
                Assert.True(client.BoolVariation("flag", Context.Builder("user2").Name("Mina").Build(), false));
                Assert.False(client.BoolVariation("flag", Context.Builder("user3").Name("Quincy").Build(), false));
            }
        }

        [Fact]
        public void NonBooleanFlags()
        {
            _td.Update(_td.Flag("flag").Variations(LdValue.Of("red"), LdValue.Of("green"), LdValue.Of("blue"))
                .OffVariation(0).FallthroughVariation(2)
                .VariationForUser("user1", 1)
                .IfMatch("name", LdValue.Of("Mina")).ThenReturn(1));

            using (var client = new LdClient(_config))
            {
                Assert.Equal("green", client.StringVariation("flag", Context.Builder("user1").Name("Lucy").Build(), ""));
                Assert.Equal("green", client.StringVariation("flag", Context.Builder("user2").Name("Mina").Build(), ""));
                Assert.Equal("blue", client.StringVariation("flag", Context.Builder("user3").Name("Quincy").Build(), ""));

                _td.Update(_td.Flag("flag").On(false));

                Assert.Equal("red", client.StringVariation("flag", Context.Builder("user1").Name("Lucy").Build(), ""));
            }
        }

        [Fact]
        public void CanUpdateStatus()
        {
            using (var client = new LdClient(_config))
            {
                Assert.Equal(DataSourceState.Valid, client.DataSourceStatusProvider.Status.State);

                var ei = DataSourceStatus.ErrorInfo.FromHttpError(500, false);
                _td.UpdateStatus(DataSourceState.Interrupted, ei);

                Assert.Equal(DataSourceState.Interrupted, client.DataSourceStatusProvider.Status.State);
                Assert.Equal(ei, client.DataSourceStatusProvider.Status.LastError);
            }
        }

        [Fact]
        public void DataSourcePropagatesToMultipleClients()
        {
            _td.Update(_td.Flag("flag").On(true));

            using (var client1 = new LdClient(_config))
            {
                using (var client2 = new LdClient(_config))
                {
                    Assert.True(client1.BoolVariation("flag", _user, false));
                    Assert.True(client2.BoolVariation("flag", _user, false));

                    _td.Update(_td.Flag("flag").On(false));

                    Assert.False(client1.BoolVariation("flag", _user, false));
                    Assert.False(client2.BoolVariation("flag", _user, false));
                }
            }
        }

        [Fact]
        public void RulesWithContextKindEvaluateCorrectly()
        {
            var companyKind = ContextKind.Of("company");
            var orgKind = ContextKind.Of("org");

            // Create flag with rules targeting specific context kinds
            _td.Update(_td.Flag("company-flag")
                .BooleanFlag()
                .FallthroughVariation(false)
                .IfMatchContext(companyKind, "name", LdValue.Of("Acme"))
                .ThenReturn(true));

            using (var client = new LdClient(_config))
            {
                // Matching company context returns true
                var matchingCompany = Context.Builder("company-123")
                    .Kind(companyKind)
                    .Set("name", "Acme")
                    .Build();
                Assert.True(client.BoolVariation("company-flag", matchingCompany, false));

                // Non-matching company context returns false
                var nonMatchingCompany = Context.Builder("company-456")
                    .Kind(companyKind)
                    .Set("name", "OtherCorp")
                    .Build();
                Assert.False(client.BoolVariation("company-flag", nonMatchingCompany, false));

                // User context with same attribute value returns false (different kind)
                var userContext = Context.Builder("user-123")
                    .Set("name", "Acme")
                    .Build();
                Assert.False(client.BoolVariation("company-flag", userContext, false));

                // Multi-context with matching company returns true
                var multiContext = Context.NewMulti(
                    Context.New("user-123"),
                    Context.Builder("company-123").Kind(companyKind).Set("name", "Acme").Build()
                );
                Assert.True(client.BoolVariation("company-flag", multiContext, false));
            }

            // Test multi-kind rule (AND condition across different context kinds)
            _td.Update(_td.Flag("multi-kind-flag")
                .BooleanFlag()
                .FallthroughVariation(false)
                .IfMatchContext(companyKind, "name", LdValue.Of("Acme"))
                .AndMatchContext(orgKind, "tier", LdValue.Of("premium"))
                .ThenReturn(true));

            using (var client = new LdClient(_config))
            {
                // Both conditions match
                var matchingMulti = Context.NewMulti(
                    Context.Builder("company-123").Kind(companyKind).Set("name", "Acme").Build(),
                    Context.Builder("org-456").Kind(orgKind).Set("tier", "premium").Build()
                );
                Assert.True(client.BoolVariation("multi-kind-flag", matchingMulti, false));

                // Only company matches
                var onlyCompanyMatches = Context.NewMulti(
                    Context.Builder("company-123").Kind(companyKind).Set("name", "Acme").Build(),
                    Context.Builder("org-456").Kind(orgKind).Set("tier", "standard").Build()
                );
                Assert.False(client.BoolVariation("multi-kind-flag", onlyCompanyMatches, false));

                // Only org matches
                var onlyOrgMatches = Context.NewMulti(
                    Context.Builder("company-123").Kind(companyKind).Set("name", "OtherCorp").Build(),
                    Context.Builder("org-456").Kind(orgKind).Set("tier", "premium").Build()
                );
                Assert.False(client.BoolVariation("multi-kind-flag", onlyOrgMatches, false));

                // Neither matches
                var neitherMatches = Context.NewMulti(
                    Context.Builder("company-123").Kind(companyKind).Set("name", "OtherCorp").Build(),
                    Context.Builder("org-456").Kind(orgKind).Set("tier", "standard").Build()
                );
                Assert.False(client.BoolVariation("multi-kind-flag", neitherMatches, false));
            }
        }
    }
}
