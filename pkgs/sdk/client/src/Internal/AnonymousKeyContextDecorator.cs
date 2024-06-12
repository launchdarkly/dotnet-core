using System;
using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.Sdk.Client.Internal.DataStores;

namespace LaunchDarkly.Sdk.Client.Internal
{
    internal class AnonymousKeyContextDecorator
    {
        private readonly PersistentDataStoreWrapper _store;
        private readonly bool _generateAnonymousKeys;

        private Dictionary<ContextKind, string> _cachedGeneratedKey = new Dictionary<ContextKind, string>();
        private object _generatedKeyLock = new object();

        public AnonymousKeyContextDecorator(
            PersistentDataStoreWrapper store,
            bool generateAnonymousKeys
            )
        {
            _store = store;
            _generateAnonymousKeys = generateAnonymousKeys;
        }

        public Context DecorateContext(Context context)
        {
            if (!_generateAnonymousKeys)
            {
                return context;
            }
            if (context.Multiple)
            {
                if (context.MultiKindContexts.Any(c => c.Anonymous))
                {
                    var builder = Context.MultiBuilder();
                    foreach (var c in context.MultiKindContexts)
                    {
                        builder.Add(c.Anonymous ? SingleKindContextWithGeneratedKey(c) : c);
                    }
                    return builder.Build();
                }
            }
            else if (context.Anonymous)
            {
                return SingleKindContextWithGeneratedKey(context);
            }
            return context;
        }

        private Context SingleKindContextWithGeneratedKey(Context context) =>
            Context.BuilderFromContext(context).Key(GetOrCreateAutoContextKey(context.Kind)).Build();

        private string GetOrCreateAutoContextKey(ContextKind contextKind)
        {
            lock (_generatedKeyLock)
            {
                if (_cachedGeneratedKey.TryGetValue(contextKind, out var key))
                {
                    return key;
                }
                var uniqueId = _store?.GetGeneratedContextKey(contextKind);
                if (uniqueId is null)
                {
                    uniqueId = Guid.NewGuid().ToString();
                    _store?.SetGeneratedContextKey(contextKind, uniqueId);
                }
                _cachedGeneratedKey[contextKind] = uniqueId;
                return uniqueId;
            }
        }
    }
}
