using System;
using System.IO;
using System.IO.IsolatedStorage;
using LaunchDarkly.Sdk.Client.Subsystems;

namespace LaunchDarkly.Sdk.Client.PlatformSpecific
{
    // In .NET Standard 2.0, we use the IsolatedStorage API to store per-user data. The .NET Standard implementation
    // of IsolatedStorage puts these files under ~/.local/share/IsolatedStorage followed by a subpath of obfuscated
    // strings that are apparently based on the application and assembly name, so the data should be specific to both
    // the OS user account and the current app.
    //
    // This is based on the Plugin.Settings plugin, but greatly simplified since we only need one data type. 
    // See: https://github.com/jamesmontemagno/SettingsPlugin/blob/master/src/Plugin.Settings/Settings.dotnet.cs

    internal sealed partial class LocalStorage : IPersistentDataStore
    {
        public string GetValue(string storageNamespace, string key)
        {
            return WithStore(store =>
            {
                try
                {
                    using (var stream = store.OpenFile(MakeFilePath(storageNamespace, key), FileMode.Open))
                    {
                        using (var sr = new StreamReader(stream))
                        {
                            return sr.ReadToEnd();
                        }
                    }
                }
                // ignore exceptions that just indicate no value has been set for this namespace/key
                catch (IsolatedStorageException e) when (e.InnerException is DirectoryNotFoundException) { }
                catch (IsolatedStorageException e) when (e.InnerException is FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                catch (FileNotFoundException) { }
                return null;
            });
        }

        public void SetValue(string storageNamespace, string key, string value)
        {
            WithStore(store =>
            {
                var filePath = MakeFilePath(storageNamespace, key);
                if (value is null)
                {
                    try
                    {
                        store.DeleteFile(filePath);
                    }
                    catch (IsolatedStorageException) { } // file didn't exist - that's OK
                }
                else
                {
                    store.CreateDirectory(storageNamespace); // has no effect if directory already exists
                    // Don't use FileMode.Create here: it replaces the file and drops NTFS alternate
                    // data streams, which due to previous oversight is where a legacy generated key can
                    // live on modern .NET on Windows. OpenOrCreate keeps the existing file and its
                    // data streams.
                    using (var stream = store.OpenFile(filePath, FileMode.OpenOrCreate, FileAccess.Write))
                    {
                        // SetLength(0) clears the old contents of the primary data stream before we write the new value.
                        stream.SetLength(0);
                        using (var sw = new StreamWriter(stream))
                        {
                            sw.Write(value);
                        }
                    }
                }
            });
        }

        private T WithStore<T>(Func<IsolatedStorageFile, T> callback)
        {
            // GetUserStoreForDomain returns a storage object that is specific to the current application and OS user.
            using (var store = IsolatedStorageFile.GetUserStoreForDomain())
            {
                return callback(store);
            }
        }

        private void WithStore(Action<IsolatedStorageFile> callback)
        {
            WithStore<bool>(store =>
            {
                callback(store);
                return true;
            });
        }

        private static string MakeFilePath(string storageNamespace, string key) =>
            storageNamespace + "/" + key;
    }
}
