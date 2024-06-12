using System.Linq;
using LaunchDarkly.Sdk.Client.Internal.DataStores;
using LaunchDarkly.Sdk.Client.Subsystems;
using Xunit;

namespace LaunchDarkly.Sdk.Client.Internal
{
    public class AnonymousKeyContextDecoratorTest : BaseTest
    {
        private static readonly ContextKind Kind1 = ContextKind.Of("kind1");
        private static readonly ContextKind Kind2 = ContextKind.Of("kind2");

        [Fact]
        public void SingleKindNonAnonymousContextIsUnchanged()
        {
            var context = Context.Builder("key1").Name("name").Build();

            AssertHelpers.ContextsEqual(context,
                MakeDecoratorWithoutPersistence().DecorateContext(context));
        }

        [Fact]
        public void SingleKindAnonymousContextIsUnchangedIfConfigOptionIsNotSet()
        {
            var context = Context.Builder("key1").Anonymous(true).Name("name").Build();

            AssertHelpers.ContextsEqual(context,
                MakeDecoratorWithoutPersistence().DecorateContext(context));
        }

        [Fact]
        public void SingleKindAnonymousContextGetsGeneratedKeyIfConfigOptionIsSet()
        {
            var context = TestUtil.BuildAutoContext().Name("name").Build();

            var transformed = MakeDecoratorWithoutPersistence(true).DecorateContext(context);

            AssertContextHasBeenTransformedWithNewKey(context, transformed);
        }

        [Fact]
        public void MultiKindContextIsUnchangedIfNoIndividualContextsNeedGeneratedKey()
        {
            var c1 = Context.Builder("key1").Kind(Kind1).Name("name1").Build();
            var c2 = Context.Builder("key2").Kind(Kind2).Anonymous(true).Name("name2").Build();

            var multiContext = Context.NewMulti(c1, c2);

            AssertHelpers.ContextsEqual(multiContext,
                MakeDecoratorWithoutPersistence().DecorateContext(multiContext));
        }

        [Fact]
        public void MultiKindContextGetsGeneratedKeyForIndividualContext()
        {
            var c1 = Context.Builder("key1").Kind(Kind1).Name("name1").Build();
            var c2 = TestUtil.BuildAutoContext().Kind(Kind2).Anonymous(true).Name("name2").Build();
            var multiContext = Context.NewMulti(c1, c2);
            var transformedMulti = MakeDecoratorWithoutPersistence(true).DecorateContext(multiContext);

            Assert.Equal(multiContext.MultiKindContexts.Select(c => c.Kind).ToList(),
                transformedMulti.MultiKindContexts.Select(c => c.Kind).ToList());

            transformedMulti.TryGetContextByKind(c1.Kind, out var c1Transformed);
            AssertHelpers.ContextsEqual(c1, c1Transformed);

            transformedMulti.TryGetContextByKind(c2.Kind, out var c2Transformed);
            AssertContextHasBeenTransformedWithNewKey(c2, c2Transformed);

            AssertHelpers.ContextsEqual(Context.NewMulti(c1, c2Transformed), transformedMulti);
        }

        [Fact]
        public void MultiKindContextGetsSeparateGeneratedKeyForEachKind()
        {
            var c1 = TestUtil.BuildAutoContext().Kind(Kind1).Anonymous(true).Name("name1").Build();
            var c2 = TestUtil.BuildAutoContext().Kind(Kind2).Anonymous(true).Name("name2").Build();
            var multiContext = Context.NewMulti(c1, c2);
            var transformedMulti = MakeDecoratorWithoutPersistence(true).DecorateContext(multiContext);

            Assert.Equal(multiContext.MultiKindContexts.Select(c => c.Kind).ToList(),
                transformedMulti.MultiKindContexts.Select(c => c.Kind).ToList());

            transformedMulti.TryGetContextByKind(c1.Kind, out var c1Transformed);
            AssertContextHasBeenTransformedWithNewKey(c1, c1Transformed);

            transformedMulti.TryGetContextByKind(c2.Kind, out var c2Transformed);
            AssertContextHasBeenTransformedWithNewKey(c2, c2Transformed);

            Assert.NotEqual(c1Transformed.Key, c2Transformed.Key);

            AssertHelpers.ContextsEqual(Context.NewMulti(c1Transformed, c2Transformed), transformedMulti);
        }

        [Fact]
        public void GeneratedKeysPersistPerKindIfPersistentStorageIsEnabled()
        {
            var c1 = TestUtil.BuildAutoContext().Kind(Kind1).Anonymous(true).Name("name1").Build();
            var c2 = TestUtil.BuildAutoContext().Kind(Kind2).Anonymous(true).Name("name2").Build();
            var multiContext = Context.NewMulti(c1, c2);

            var store = new MockPersistentDataStore();

            var decorator1 = MakeDecoratorWithPersistence(store, true);

            var transformedMultiA = decorator1.DecorateContext(multiContext);
            transformedMultiA.TryGetContextByKind(c1.Kind, out var c1Transformed);
            AssertContextHasBeenTransformedWithNewKey(c1, c1Transformed);
            transformedMultiA.TryGetContextByKind(c2.Kind, out var c2Transformed);
            AssertContextHasBeenTransformedWithNewKey(c2, c2Transformed);

            var decorator2 = MakeDecoratorWithPersistence(store, true);

            var transformedMultiB = decorator2.DecorateContext(multiContext);
            AssertHelpers.ContextsEqual(transformedMultiA, transformedMultiB);
        }

        [Fact]
        public void GeneratedKeysAreReusedDuringLifetimeOfSdkEvenIfPersistentStorageIsDisabled()
        {
            var c1 = TestUtil.BuildAutoContext().Kind(Kind1).Anonymous(true).Name("name1").Build();
            var c2 = TestUtil.BuildAutoContext().Kind(Kind2).Anonymous(true).Name("name2").Build();
            var multiContext = Context.NewMulti(c1, c2);

            var store = new MockPersistentDataStore();

            var decorator = MakeDecoratorWithoutPersistence(true);

            var transformedMultiA = decorator.DecorateContext(multiContext);
            transformedMultiA.TryGetContextByKind(c1.Kind, out var c1Transformed);
            AssertContextHasBeenTransformedWithNewKey(c1, c1Transformed);
            transformedMultiA.TryGetContextByKind(c2.Kind, out var c2Transformed);
            AssertContextHasBeenTransformedWithNewKey(c2, c2Transformed);

            var transformedMultiB = decorator.DecorateContext(multiContext);
            AssertHelpers.ContextsEqual(transformedMultiA, transformedMultiB);
        }

        [Fact]
        public void GeneratedKeysAreNotReusedAcrossRestartsIfPersistentStorageIsDisabled()
        {
            var c1 = TestUtil.BuildAutoContext().Kind(Kind1).Anonymous(true).Name("name1").Build();
            var c2 = TestUtil.BuildAutoContext().Kind(Kind2).Anonymous(true).Name("name2").Build();
            var multiContext = Context.NewMulti(c1, c2);

            var decorator1 = MakeDecoratorWithoutPersistence(true);

            var transformedMultiA = decorator1.DecorateContext(multiContext);
            transformedMultiA.TryGetContextByKind(c1.Kind, out var c1TransformedA);
            AssertContextHasBeenTransformedWithNewKey(c1, c1TransformedA);
            transformedMultiA.TryGetContextByKind(c2.Kind, out var c2TransformedA);
            AssertContextHasBeenTransformedWithNewKey(c2, c2TransformedA);

            var decorator2 = MakeDecoratorWithoutPersistence(true);

            var transformedMultiB = decorator2.DecorateContext(multiContext);
            transformedMultiB.TryGetContextByKind(c1.Kind, out var c1TransformedB);
            AssertContextHasBeenTransformedWithNewKey(c1, c1TransformedB);
            Assert.NotEqual(c1TransformedA.Key, c1TransformedB.Key);
            transformedMultiB.TryGetContextByKind(c2.Kind, out var c2TransformedB);
            AssertContextHasBeenTransformedWithNewKey(c2, c2TransformedB);
            Assert.NotEqual(c2TransformedA.Key, c2TransformedB.Key);
        }

        private AnonymousKeyContextDecorator MakeDecoratorWithPersistence(IPersistentDataStore store, bool generateAnonymousKeys = false) =>
            new AnonymousKeyContextDecorator(new PersistentDataStoreWrapper(store, BasicMobileKey, testLogger), generateAnonymousKeys);

        private AnonymousKeyContextDecorator MakeDecoratorWithoutPersistence(bool generateAnonymousKeys = false) =>
            MakeDecoratorWithPersistence(new NullPersistentDataStore(), generateAnonymousKeys);

        private void AssertContextHasBeenTransformedWithNewKey(Context original, Context transformed)
        {
            Assert.NotEqual(original.Key, transformed.Key);
            AssertHelpers.ContextsEqual(Context.BuilderFromContext(original).Key(transformed.Key).Build(),
                transformed);
        }
    }
}
