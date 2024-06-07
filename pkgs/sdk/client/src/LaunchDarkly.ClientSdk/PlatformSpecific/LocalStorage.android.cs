using Android.App;
using Android.Content;
using Android.Preferences;
using LaunchDarkly.Sdk.Client.Subsystems;

namespace LaunchDarkly.Sdk.Client.PlatformSpecific
{
    internal sealed partial class LocalStorage : IPersistentDataStore
    {
        public string GetValue(string storageNamespace, string key)
        {
            using (var sharedPreferences = GetSharedPreferences(storageNamespace))
            {
                return sharedPreferences.GetString(key, null);
            }
        }

        public void SetValue(string storageNamespace, string key, string value)
        {
            using (var sharedPreferences = GetSharedPreferences(storageNamespace))
            using (var editor = sharedPreferences.Edit())
            {
                if (value is null)
                {
                    editor.Remove(key).Commit();
                }
                else
                {
                    editor.PutString(key, value).Commit();
                }
            }
        }

        static ISharedPreferences GetSharedPreferences(string sharedName)
        {
            var context = Application.Context;

            return sharedName is null ?
                PreferenceManager.GetDefaultSharedPreferences(context) :
                    context.GetSharedPreferences(sharedName, FileCreationMode.Private);
        }
    }
}
