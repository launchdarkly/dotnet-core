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
            HandleErrorsAndLock(() =>
            {
                var keyName = KeyForGeneratedContextKey(contextKind);
                var value = _persistentStore.GetValue(_globalNamespace, keyName);

                // A stored generated key is always a non-empty GUID, so treat an empty value
                // the same as null and never return it as a key. Besides the normal
                // "nothing stored yet" (null), an empty value can be read back from a
                // zero-length entry left behind by a legacy ':' key format handled below.
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }

                // Legacy read: ClientSdk 3.0.0 through 5.10.2 stored non-default-kind keys under a
                // name containing ':' (e.g. "anonUser:ld_device"), which violates the key
                // contract and fails outright on some stores (.NET Framework IsolatedStorage);
                // on Windows under modern .NET it instead lands as an NTFS alternate data stream
                // on a zero-length "anonUser" base file. Copy any legacy value forward to the
                // new, contract-safe name so the generated key stays stable across the upgrade.
                // We copy rather than move so that an install upgraded from a legacy entry stays
                // downgrade-safe (an older SDK still reads the ':' key); a fresh install on the 
                // new format writes no legacy entry, so a later downgrade there regenerates the key.
                var legacyValue = GetLegacyGeneratedContextKey(contextKind);
                if (!string.IsNullOrEmpty(legacyValue))
                {
                    try
                    {
                        // copy legacy value forward
                        _persistentStore.SetValue(_globalNamespace, keyName, legacyValue);
                    }
                    catch (Exception e)
                    {
                        // Best-effort: if the copy can't be persisted now, we still return the
                        // stable legacy value and retry the copy on a later read.
                        _log.Debug("Could not persist migrated generated context key; will retry in future: {0}", e.Message);
                    }
                    return legacyValue;
                }

                return null;
            });

        public void SetGeneratedContextKey(ContextKind contextKind, string value) =>
            HandleErrorsAndLock(() => _persistentStore.SetValue(_globalNamespace,
                KeyForGeneratedContextKey(contextKind), value));

        public void Dispose() =>
            _persistentStore.Dispose();

        private static string KeyForContextId(string contextId) => EnvironmentContextDataKeyPrefix + contextId;

        // The default kind keeps the bare "anonUser" name (unchanged, contract-safe). For any
        // other kind the kind value is base64url-encoded (no padding) so the key contains only
        // contract-legal characters - unlike the old "anonUser:<kind>" form, whose ':' broke
        // the key contract and .NET Framework IsolatedStorage paths.
        private static string KeyForGeneratedContextKey(ContextKind contextKind) =>
            contextKind.IsDefault
                ? GlobalAnonContextKey
                : GlobalAnonContextKey + "_" + contextKind.Value.UrlSafeEncodeNoPadding();

        // The pre-migration key name for a non-default kind. Only used to read values written
        // by older SDK versions; never written to.
        private static string LegacyKeyForGeneratedContextKey(ContextKind contextKind) =>
            GlobalAnonContextKey + ":" + contextKind.Value;

        // Reads a value stored under the legacy ':'-containing key name. The caller must hold
        // the store lock (this is invoked from within GetGeneratedContextKey's locked action).
        // A failure here is expected on stores that reject ':' (notably .NET Framework
        // IsolatedStorage) and is swallowed quietly: it deliberately does NOT go through
        // MaybeLogStoreError, so this one-time migration probe can't consume the single
        // storage-error log slot and mask a genuine, later persistence failure.
        private string GetLegacyGeneratedContextKey(ContextKind contextKind)
        {
            try
            {
                return _persistentStore.GetValue(_globalNamespace, LegacyKeyForGeneratedContextKey(contextKind));
            }
            catch (Exception e)
            {
                _log.Debug("Ignoring expected error reading legacy generated context key: {0}", e.Message);
                return null;
            }
        }

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
