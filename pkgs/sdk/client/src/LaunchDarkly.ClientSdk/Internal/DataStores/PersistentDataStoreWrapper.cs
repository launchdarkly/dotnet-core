using System;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Internal;
using LaunchDarkly.Sdk.Internal.Concurrent;
using LaunchDarkly.Sdk.Client.Subsystems;

using static LaunchDarkly.Sdk.Client.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Client.Internal.DataStores
{
    /// <summary>
    /// A facade over some implementation of <see cref="IPersistentDataStore"/>, which adds
    /// behavior that should be the same for all implementations, such as the specific data
    /// keys we use, the logging of errors, and how data is serialized and deserialized. This
    /// allows FlagDataManager (and other parts of the SDK that may need to access persistent
    /// storage) to be written in a clearer way without embedding many implementation details.
    /// </summary>
    /// <remarks>
    /// See <see cref="IPersistentDataStore"/> for the rules about what namespaces and keys
    /// we can use. It is <see cref="PersistentDataStoreWrapper"/>'s responsibility to follow
    /// those rules. We are OK as long as we use base64url-encoding for all variables such as
    /// user key and mobile key, and use only characters from the base64url set (A-Z, a-z,
    /// 0-9, -, and _) for other namespace/key components.
    /// </remarks>
    internal sealed class PersistentDataStoreWrapper : IDisposable
    {
        private const string NamespacePrefix = "LaunchDarkly";
        private const string GlobalAnonContextKey = "anonUser";
        private const string EnvironmentMetadataKey = "index";
        private const string EnvironmentContextDataKeyPrefix = "flags_";

        private readonly IPersistentDataStore _persistentStore;
        private readonly string _globalNamespace;
        private readonly string _environmentNamespace;

        private readonly Logger _log;
        private readonly object _storeLock = new object();
        private readonly AtomicBoolean _loggedStorageError = new AtomicBoolean(false);

        public PersistentDataStoreWrapper(
            IPersistentDataStore persistentStore,
            string mobileKey,
            Logger log
            )
        {
            _persistentStore = persistentStore;
            _log = log;

            _globalNamespace = NamespacePrefix;
            _environmentNamespace = NamespacePrefix + "_" + Base64.UrlSafeSha256Hash(mobileKey);
        }

        public FullDataSet? GetContextData(string contextId)
        {
            var serializedData = HandleErrorsAndLock(() => _persistentStore.GetValue(_environmentNamespace, KeyForContextId(contextId)));
            if (serializedData is null)
            {
                return null;
            }
            try
            {
                return DataModelSerialization.DeserializeAll(serializedData);
            }
            catch (Exception e)
            {
                LogHelpers.LogException(_log, "Failed to deserialize data from persistent store", e);
                return null;
            }
        }

        public void SetContextData(string contextId, FullDataSet data) =>
            HandleErrorsAndLock(() => _persistentStore.SetValue(_environmentNamespace, KeyForContextId(contextId),
                DataModelSerialization.SerializeAll(data)));

        public void RemoveContextData(string contextId) =>
            HandleErrorsAndLock(() => _persistentStore.SetValue(_environmentNamespace, KeyForContextId(contextId), null));

        public ContextIndex GetIndex()
        {
            string data = HandleErrorsAndLock(() => _persistentStore.GetValue(_environmentNamespace, EnvironmentMetadataKey));
            if (data is null)
            {
                return new ContextIndex();
            }
            try
            {
                return ContextIndex.Deserialize(data);
            }
            catch (Exception)
            {
                _log.Warn("Discarding invalid data from persistent store index");
                return new ContextIndex();
            }
        }

        public void SetIndex(ContextIndex index) =>
            HandleErrorsAndLock(() => _persistentStore.SetValue(_environmentNamespace, EnvironmentMetadataKey, index.Serialize()));

        public string GetGeneratedContextKey(ContextKind contextKind) =>
            HandleErrorsAndLock(() => _persistentStore.GetValue(_globalNamespace, KeyForGeneratedContextKey(contextKind)));

        public void SetGeneratedContextKey(ContextKind contextKind, string value) =>
            HandleErrorsAndLock(() => _persistentStore.SetValue(_globalNamespace,
                KeyForGeneratedContextKey(contextKind), value));

        public void Dispose() =>
            _persistentStore.Dispose();

        private static string KeyForContextId(string contextId) => EnvironmentContextDataKeyPrefix + contextId;

        private static string KeyForGeneratedContextKey(ContextKind contextKind) =>
            contextKind.IsDefault ? GlobalAnonContextKey : (GlobalAnonContextKey + ":" + contextKind.Value);

        private void MaybeLogStoreError(Exception e)
        {
            if (!_loggedStorageError.GetAndSet(true))
            {
                LogHelpers.LogException(_log, "Failure in persistent data store", e);
            }
        }

        private T HandleErrorsAndLock<T>(Func<T> action)
        {
            try
            {
                lock (_storeLock)
                {
                    return action();
                }
            }
            catch (Exception e)
            {
                MaybeLogStoreError(e);
                return default(T);
            }
        }

        private void HandleErrorsAndLock(Action action)
        {
            _ = HandleErrorsAndLock<bool>(() => { action(); return true; });
        }
    }
}
