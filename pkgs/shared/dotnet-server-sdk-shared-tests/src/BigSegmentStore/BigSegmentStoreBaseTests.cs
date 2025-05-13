using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Subsystems;
using Xunit;
using Xunit.Abstractions;

using static LaunchDarkly.Sdk.Server.Subsystems.BigSegmentStoreTypes;

namespace LaunchDarkly.Sdk.Server.SharedTests.BigSegmentStore
{
    /// <summary>
    /// A configurable Xunit test class for all implementations of <c>IBigSegmentStore</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each implementation of those interfaces should define a test class that is a subclass of this
    /// class for their implementation type, and run it in the unit tests for their project.
    /// </para>
    /// <para>
    /// You must override the <see cref="Configuration"/> property to provide details specific to
    /// your implementation type.
    /// </para>
    /// </remarks>
    public abstract class BigSegmentStoreBaseTests
    {
        /// <summary>
        /// Override this method to create the configuration for the test suite.
        /// </summary>
        protected abstract BigSegmentStoreTestConfig Configuration { get; }

        private const string prefix = "testprefix";
        private const string fakeUserHash = "userhash";
        private const string segmentRef1 = "key1", segmentRef2 = "key2", segmentRef3 = "key3";
        private static readonly string[] allSegmentRefs = new string[] { segmentRef1, segmentRef2, segmentRef3 };

        private readonly ILogAdapter _testLogging;

        protected BigSegmentStoreBaseTests()
        {
            _testLogging = Logs.None;
        }

        protected BigSegmentStoreBaseTests(ITestOutputHelper testOutput)
        {
            _testLogging = TestLogging.TestOutputAdapter(testOutput);
        }

        private IBigSegmentStore MakeStore()
        {
            var context = new LdClientContext("sdk-key", null, null,
                Components.HttpConfiguration().Build(new LdClientContext("sdk-key")), _testLogging.Logger(""), false, null);
            return Configuration.StoreFactoryFunc(prefix).Build(context);
        }

        private async Task<IBigSegmentStore> MakeEmptyStore()
        {
            var store = MakeStore();
            try
            {
                await Configuration.ClearDataAction(prefix);
            }
            catch
            {
                store.Dispose();
                throw;
            }
            return store;
        }

        [Fact]
        public async void MissingMetadata()
        {
            using (var store = await MakeEmptyStore())
            {
                Assert.Null(await store.GetMetadataAsync());
            }
        }

        [Fact]
        public async void ValidMetadata()
        {
            using (var store = await MakeEmptyStore())
            {
                var metadata = new StoreMetadata { LastUpToDate = UnixMillisecondTime.Now };
                await Configuration.SetMetadataAction(prefix, metadata);

                var result = await store.GetMetadataAsync();
                Assert.NotNull(result);
                Assert.Equal(metadata.LastUpToDate, result.Value.LastUpToDate);
            }
        }

        [Fact]
        public async void MembershipNotFound()
        {
            using (var store = await MakeEmptyStore())
            {
                var membership = await store.GetMembershipAsync(fakeUserHash);

                // Either null or an empty membership is allowed in this case
                if (membership != null)
                {
                    AssertEqualMembership(NewMembershipFromSegmentRefs(null, null), membership);
                }
            }
        }

        [Theory]
        [InlineData(new string[] { segmentRef1 }, new string[] { })]
        [InlineData(new string[] { segmentRef1, segmentRef2 }, new string[] { })]
        [InlineData(new string[] { }, new string[] { segmentRef1 })]
        [InlineData(new string[] { }, new string[] { segmentRef1, segmentRef2 })]
        [InlineData(new string[] { segmentRef1, segmentRef2 }, new string[] { segmentRef2, segmentRef3 })]
        public async void MembershipFound(string[] includes, string[] excludes)
        {
            using (var store = await MakeEmptyStore())
            {
                await Configuration.SetSegmentsAction(prefix, fakeUserHash, includes, excludes);

                var membership = await store.GetMembershipAsync(fakeUserHash);

                AssertEqualMembership(NewMembershipFromSegmentRefs(includes, excludes), membership);
            }
        }

        private static void AssertEqualMembership(IMembership expected, IMembership actual)
        {
            if (actual.GetType().FullName.StartsWith("LaunchDarkly.Sdk.Server.Internal.BigSegments.MembershipBuilder"))
            {
                // The store implementation is using our standard membership types, so we can rely on the
                // standard equality test for those
                Assert.Equal(expected, actual);
            }
            else
            {
                // The store implementation has implemented IMembership in some other way, so we have to
                // check for the inclusion or exclusion of specific keys
                foreach (var segmentRef in allSegmentRefs)
                {
                    if (actual.CheckMembership(segmentRef) != expected.CheckMembership(segmentRef))
                    {
                        Assert.True(false, string.Format("expected membership for {0} to be {1} but was {2}",
                            segmentRef, expected.CheckMembership(segmentRef), actual.CheckMembership(segmentRef)));
                    }
                }
            }
        }
    }
}
