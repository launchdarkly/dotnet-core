using Foundation;
using LaunchDarkly.Sdk.Client.Subsystems;

namespace LaunchDarkly.Sdk.Client.PlatformSpecific
{
    internal sealed partial class LocalStorage : IPersistentDataStore
    {
        private const string LaunchDarklyMainKey = "com.launchdarkly.sdk";
        
        public string GetValue(string storageNamespace, string key)
        {
            using (var defaults = NSUserDefaults.StandardUserDefaults)
            {
                var mainDict = defaults.DictionaryForKey(LaunchDarklyMainKey);
                if (mainDict is null)
                {
                    return null;
                }
                var groupDict = mainDict.ObjectForKey(new NSString(storageNamespace)) as NSDictionary;
                if (groupDict is null)
                {
                    return null;
                }
                var value = groupDict.ObjectForKey(new NSString(key));
                return value?.ToString();
            }
        }

        public void SetValue(string storageNamespace, string key, string value)
        {
            using (var defaults = NSUserDefaults.StandardUserDefaults)
            {
                var mainDict = defaults.DictionaryForKey(LaunchDarklyMainKey) as NSDictionary;
                var newMainDict = mainDict is null ? new NSMutableDictionary() :
                    new NSMutableDictionary(mainDict);

                var groupKey = new NSString(storageNamespace);
                var groupDict = newMainDict.ObjectForKey(groupKey) as NSDictionary;
                var newGroupDict = groupDict is null ? new NSMutableDictionary() :
                    new NSMutableDictionary(groupDict);
                
                if (value is null)
                {
                    newGroupDict.Remove(new NSString(key));
                }
                else
                {
                    newGroupDict.SetValueForKey(new NSString(value), new NSString(key));
                }

                newMainDict.SetValueForKey(newGroupDict, groupKey);
                defaults.SetValueForKey(newMainDict, new NSString(LaunchDarklyMainKey));
            }
        }
    }
}
