using System;
using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.EnvReporting;
using LaunchDarkly.Sdk.Client.Internal.DataStores;

namespace LaunchDarkly.Sdk.Client.Internal
{
    /// <summary>
    /// This class can decorate a context by adding additional contexts to it using auto environment attributes
    /// provided by <see cref="IEnvironmentReporter"/>.
    /// </summary>
    internal class AutoEnvContextDecorator
    {
        internal const string LdApplicationKind = "ld_application";
        internal const string LdDeviceKind = "ld_device";
        internal const string AttrId = "id";
        internal const string AttrName = "name";
        internal const string AttrVersion = "version";
        internal const string AttrVersionName = "versionName";
        internal const string AttrManufacturer = "manufacturer";
        internal const string AttrModel = "model";
        internal const string AttrLocale = "locale";
        internal const string AttrOs = "os";
        internal const string AttrFamily = "family";
        internal const string EnvAttributesVersion = "envAttributesVersion";
        internal const string SpecVersion = "1.0";

        private readonly PersistentDataStoreWrapper _persistentData;
        private readonly IEnvironmentReporter _environmentReporter;
        private readonly Logger _logger;

        /// <summary>
        /// Creates a <see cref="AutoEnvContextDecorator"/>.
        /// </summary>
        /// <param name="persistentData">the data source that will be used for retrieving/saving information related
        /// to the generated contexts.  Example data includes the stable key of the ld_device context kind.</param>
        /// <param name="environmentReporter">the environment reporter that will be used to source the
        /// environment attributes</param>
        /// <param name="logger">the logger</param>
        public AutoEnvContextDecorator(
            PersistentDataStoreWrapper persistentData,
            IEnvironmentReporter environmentReporter,
            Logger logger)
        {
            _persistentData = persistentData;
            _environmentReporter = environmentReporter;
            _logger = logger;
        }

        /// <summary>
        /// Decorates the provided context with additional contexts containing environment attributes.
        /// </summary>
        /// <param name="context">the context to be decorated</param>
        /// <returns>the decorated context</returns>
        public Context DecorateContext(Context context)
        {
            var builder = Context.MultiBuilder();
            builder.Add(context);

            foreach (var recipe in MakeRecipeList())
            {
                if (!context.TryGetContextByKind(recipe.Kind, out _))
                {
                    // only add contexts for recipe Kinds not already in context to avoid overwriting data.
                    recipe.TryWrite(builder);
                }
                else
                {
                    _logger.Warn("Unable to automatically add environment attributes for kind:{0}. {1} already exists.",
                        recipe.Kind, recipe.Kind);
                }
            }

            return builder.Build();
        }

        private readonly struct ContextRecipe
        {
            public ContextKind Kind { get; }
            private Func<string> KeyCallable { get; }
            private List<Node> RecipeNodes { get; }

            public ContextRecipe(ContextKind kind, Func<string> keyCallable, List<Node> recipeNodes)
            {
                Kind = kind;
                KeyCallable = keyCallable;
                RecipeNodes = recipeNodes;
            }

            public void TryWrite(ContextMultiBuilder multiBuilder)
            {
                var contextBuilder = Context.Builder(Kind, KeyCallable.Invoke());
                var adaptedBuilder = new ContextBuilderAdapter(contextBuilder);
                if (RecipeNodes.Aggregate(false, (wrote, node) => wrote | node.TryWrite(adaptedBuilder)))
                {
                    contextBuilder.Set(EnvAttributesVersion, SpecVersion);
                    multiBuilder.Add(contextBuilder.Build());
                }
            }
        }

        private List<ContextRecipe> MakeRecipeList()
        {
            var ldApplicationKind = ContextKind.Of(LdApplicationKind);
            var applicationNodes = new List<Node>
            {
                new Node(AttrId, LdValue.Of(_environmentReporter.ApplicationInfo?.ApplicationId)),
                new Node(AttrName,
                    LdValue.Of(_environmentReporter.ApplicationInfo?.ApplicationName)),
                new Node(AttrVersion,
                    LdValue.Of(_environmentReporter.ApplicationInfo?.ApplicationVersion)),
                new Node(AttrVersionName,
                     LdValue.Of(_environmentReporter.ApplicationInfo?.ApplicationVersionName)),
                new Node(AttrLocale, LdValue.Of(_environmentReporter.Locale)),
            };

            var ldDeviceKind = ContextKind.Of(LdDeviceKind);
            var deviceNodes = new List<Node>
            {
                new Node(AttrManufacturer,
                    LdValue.Of(_environmentReporter.DeviceInfo?.Manufacturer)),
                new Node(AttrModel,  LdValue.Of(_environmentReporter.DeviceInfo?.Model)),
                new Node(AttrOs, new List<Node>
                {
                    new Node(AttrFamily,  LdValue.Of(_environmentReporter.OsInfo?.Family)),
                    new Node(AttrName,  LdValue.Of(_environmentReporter.OsInfo?.Name)),
                    new Node(AttrVersion,  LdValue.Of(_environmentReporter.OsInfo?.Version)),
                })
            };

            return new List<ContextRecipe>
            {
                new ContextRecipe(
                    ldApplicationKind,
                    () => Base64.UrlSafeSha256Hash(
                        _environmentReporter.ApplicationInfo?.ApplicationId ?? ""
                    ),
                    applicationNodes
                ),
                new ContextRecipe(
                    ldDeviceKind,
                    () => GetOrCreateAutoContextKey(_persistentData, ldDeviceKind),
                    deviceNodes
                )
            };
        }

        private string GetOrCreateAutoContextKey(PersistentDataStoreWrapper store, ContextKind contextKind)
        {
            var uniqueId = store.GetGeneratedContextKey(contextKind);
            if (uniqueId is null)
            {
                uniqueId = Guid.NewGuid().ToString();
                store.SetGeneratedContextKey(contextKind, uniqueId);
            }
            return uniqueId;
        }

        private interface ISettableMap
        {
            void Set(string attributeName, LdValue value);
        }

        private class Node
        {
            private readonly string _key;
            private readonly LdValue? _value;
            private readonly List<Node> _children;

            public Node(string key, List<Node> children)
            {
                _key = key;
                _children = children;
            }

            public Node(string key, LdValue value)
            {
                _key = key;
                _value = value;
            }

            public bool TryWrite(ISettableMap settableMap)
            {
                if (_value.HasValue && !_value.Value.IsNull)
                {
                    settableMap.Set(_key, _value.Value);
                    return true;
                }

                if (_children == null) return false;

                var objBuilder = LdValue.BuildObject();
                var adaptedBuilder = new ObjectBuilderAdapter(objBuilder);

                if (!_children.Aggregate(false, (wrote, node) => wrote | node.TryWrite(adaptedBuilder))) return false;

                settableMap.Set(_key, objBuilder.Build());
                return true;
            }
        }


        private class ObjectBuilderAdapter : ISettableMap
        {
            private readonly LdValue.ObjectBuilder _underlyingBuilder;

            public ObjectBuilderAdapter(LdValue.ObjectBuilder builder)
            {
                _underlyingBuilder = builder;
            }

            public void Set(string attributeName, LdValue value)
            {
                _underlyingBuilder.Set(attributeName, value);
            }
        }

        private class ContextBuilderAdapter : ISettableMap
        {
            private readonly ContextBuilder _underlyingBuilder;

            public ContextBuilderAdapter(ContextBuilder builder)
            {
                _underlyingBuilder = builder;
            }

            public void Set(string attributeName, LdValue value)
            {
                _underlyingBuilder.Set(attributeName, value);
            }
        }
    }
}
