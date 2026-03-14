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
        public void IfMatchContext_MatchingContextKind_EvaluatesToTrue()
        {
            var companyKind = ContextKind.Of("company");

            _td.Update(_td.Flag("company-flag")
                .BooleanFlag()
                .FallthroughVariation(false)
                .IfMatchContext(companyKind, "name", LdValue.Of("Acme"))
                .ThenReturn(true));

            using (var client = new LdClient(_config))
            {
                var matchingCompany = Context.Builder("company-123")
                    .Kind(companyKind)
                    .Set("name", "Acme")
                    .Build();

                Assert.True(client.BoolVariation("company-flag", matchingCompany, false));
            }
        }

        [Fact]
        public void IfMatchContext_NonMatchingAttributeValue_EvaluatesToFalse()
        {
            var companyKind = ContextKind.Of("company");

            _td.Update(_td.Flag("company-flag")
                .BooleanFlag()
                .FallthroughVariation(false)
                .IfMatchContext(companyKind, "name", LdValue.Of("Acme"))
                .ThenReturn(true));

            using (var client = new LdClient(_config))
            {
                var nonMatchingCompany = Context.Builder("company-456")
                    .Kind(companyKind)
                    .Set("name", "OtherCorp")
                    .Build();

                Assert.False(client.BoolVariation("company-flag", nonMatchingCompany, false));
            }
        }

        [Fact]
        public void IfMatchContext_WrongContextKind_EvaluatesToFalse()
        {
            var companyKind = ContextKind.Of("company");

            _td.Update(_td.Flag("company-flag")
                .BooleanFlag()
                .FallthroughVariation(false)
                .IfMatchContext(companyKind, "name", LdValue.Of("Acme"))
                .ThenReturn(true));

            using (var client = new LdClient(_config))
            {
                // User context with same attribute value should not match (wrong context kind)
                var userContext = Context.Builder("user-123")
                    .Set("name", "Acme")
                    .Build();

                Assert.False(client.BoolVariation("company-flag", userContext, false));
            }
        }

        [Fact]
        public void IfMatchContext_MultiContextWithMatchingKind_EvaluatesToTrue()
        {
            var companyKind = ContextKind.Of("company");

            _td.Update(_td.Flag("company-flag")
                .BooleanFlag()
                .FallthroughVariation(false)
                .IfMatchContext(companyKind, "name", LdValue.Of("Acme"))
                .ThenReturn(true));

            using (var client = new LdClient(_config))
            {
                // Multi-context with matching company should return true
                var multiContext = Context.NewMulti(
                    Context.New("user-123"),
                    Context.Builder("company-123").Kind(companyKind).Set("name", "Acme").Build()
                );

                Assert.True(client.BoolVariation("company-flag", multiContext, false));
            }
        }

        [Fact]
        public void AndMatchContext_BothConditionsMatch_EvaluatesToTrue()
        {
            var companyKind = ContextKind.Of("company");
            var orgKind = ContextKind.Of("org");

            _td.Update(_td.Flag("multi-kind-flag")
                .BooleanFlag()
                .FallthroughVariation(false)
                .IfMatchContext(companyKind, "name", LdValue.Of("Acme"))
                .AndMatchContext(orgKind, "tier", LdValue.Of("premium"))
                .ThenReturn(true));

            using (var client = new LdClient(_config))
            {
                var matchingMulti = Context.NewMulti(
                    Context.Builder("company-123").Kind(companyKind).Set("name", "Acme").Build(),
                    Context.Builder("org-456").Kind(orgKind).Set("tier", "premium").Build()
                );

                Assert.True(client.BoolVariation("multi-kind-flag", matchingMulti, false));
            }
        }

        [Fact]
        public void AndMatchContext_OnlyFirstConditionMatches_EvaluatesToFalse()
        {
            var companyKind = ContextKind.Of("company");
            var orgKind = ContextKind.Of("org");

            _td.Update(_td.Flag("multi-kind-flag")
                .BooleanFlag()
                .FallthroughVariation(false)
                .IfMatchContext(companyKind, "name", LdValue.Of("Acme"))
                .AndMatchContext(orgKind, "tier", LdValue.Of("premium"))
                .ThenReturn(true));

            using (var client = new LdClient(_config))
            {
                var onlyCompanyMatches = Context.NewMulti(
                    Context.Builder("company-123").Kind(companyKind).Set("name", "Acme").Build(),
                    Context.Builder("org-456").Kind(orgKind).Set("tier", "standard").Build()
                );

                Assert.False(client.BoolVariation("multi-kind-flag", onlyCompanyMatches, false));
            }
        }

        [Fact]
        public void AndMatchContext_OnlySecondConditionMatches_EvaluatesToFalse()
        {
            var companyKind = ContextKind.Of("company");
            var orgKind = ContextKind.Of("org");

            _td.Update(_td.Flag("multi-kind-flag")
                .BooleanFlag()
                .FallthroughVariation(false)
                .IfMatchContext(companyKind, "name", LdValue.Of("Acme"))
                .AndMatchContext(orgKind, "tier", LdValue.Of("premium"))
                .ThenReturn(true));

            using (var client = new LdClient(_config))
            {
                var onlyOrgMatches = Context.NewMulti(
                    Context.Builder("company-123").Kind(companyKind).Set("name", "OtherCorp").Build(),
                    Context.Builder("org-456").Kind(orgKind).Set("tier", "premium").Build()
                );

                Assert.False(client.BoolVariation("multi-kind-flag", onlyOrgMatches, false));
            }
        }

        [Fact]
        public void AndMatchContext_NeitherConditionMatches_EvaluatesToFalse()
        {
            var companyKind = ContextKind.Of("company");
            var orgKind = ContextKind.Of("org");

            _td.Update(_td.Flag("multi-kind-flag")
                .BooleanFlag()
                .FallthroughVariation(false)
                .IfMatchContext(companyKind, "name", LdValue.Of("Acme"))
                .AndMatchContext(orgKind, "tier", LdValue.Of("premium"))
                .ThenReturn(true));

            using (var client = new LdClient(_config))
            {
                var neitherMatches = Context.NewMulti(
                    Context.Builder("company-123").Kind(companyKind).Set("name", "OtherCorp").Build(),
                    Context.Builder("org-456").Kind(orgKind).Set("tier", "standard").Build()
                );

                Assert.False(client.BoolVariation("multi-kind-flag", neitherMatches, false));
            }
        }

        [Fact]
        public void AndMatchContext_TwoCustomContextKindsOnSameAttribute_EvaluatesCorrectly()
        {
            var contextA = ContextKind.Of("context_a");
            var contextB = ContextKind.Of("context_b");

            _td.Update(_td.Flag("flag_A")
                .BooleanFlag()
                .FallthroughVariation(false)
                .IfMatchContext(contextA, "key", LdValue.Of("A1"))
                .AndMatchContext(contextB, "key", LdValue.Of("B2"))
                .ThenReturn(true));

            using (var client = new LdClient(_config))
            {
                // Both contexts match - should return true
                var bothMatch = Context.NewMulti(
                    Context.Builder("A1").Kind(contextA).Build(),
                    Context.Builder("B2").Kind(contextB).Build()
                );
                Assert.True(client.BoolVariation("flag_A", bothMatch, false));

                // Only context_a matches - should return false
                var onlyAMatches = Context.NewMulti(
                    Context.Builder("A1").Kind(contextA).Build(),
                    Context.Builder("wrong").Kind(contextB).Build()
                );
                Assert.False(client.BoolVariation("flag_A", onlyAMatches, false));

                // Only context_b matches - should return false
                var onlyBMatches = Context.NewMulti(
                    Context.Builder("wrong").Kind(contextA).Build(),
                    Context.Builder("B2").Kind(contextB).Build()
                );
                Assert.False(client.BoolVariation("flag_A", onlyBMatches, false));

                // Neither matches - should return false
                var neitherMatches = Context.NewMulti(
                    Context.Builder("wrong1").Kind(contextA).Build(),
                    Context.Builder("wrong2").Kind(contextB).Build()
                );
                Assert.False(client.BoolVariation("flag_A", neitherMatches, false));

                // Wrong context kinds - should return false
                var wrongKinds = Context.NewMulti(
                    Context.Builder("A1").Build(), // user context, not context_a
                    Context.Builder("B2").Build()  // user context, not context_b
                );
                Assert.False(client.BoolVariation("flag_A", wrongKinds, false));
            }
        }
    }
}
