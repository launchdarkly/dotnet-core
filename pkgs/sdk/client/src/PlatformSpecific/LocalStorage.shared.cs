using LaunchDarkly.Sdk.Client.Subsystems;

namespace LaunchDarkly.Sdk.Client.PlatformSpecific
{
    /// <summary>
    /// Platform-specific implementations of the IPersistentDataStore interface for
    /// storing arbitrary string key-value pairs. On mobile devices, this is
    /// implemented with the native preferences API. In .NET Standard, it is
    /// implemented with the IsolatedStorage API.
    /// </summary>
    internal sealed partial class LocalStorage : IPersistentDataStore
    {
        internal static readonly LocalStorage Instance = new LocalStorage();

        public void Dispose() { }
    }
}
