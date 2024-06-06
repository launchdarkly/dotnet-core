using System;

namespace LaunchDarkly.Sdk.Client.Subsystems
{
    /// <summary>
    /// Interface for a data store that holds feature flag data and other SDK properties in a
    /// serialized form.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This interface should be used for platform-specific integrations that store data somewhere
    /// other than in memory. The SDK has a default implementation which uses the native preferences
    /// API on mobile platforms, and the .NET <c>IsolatedStorage</c> API in desktop applications. You
    /// only need to use this interface if you want to provide different storage behavior.
    /// </para>
    /// <para>
    /// Each data item is uniquely identified by the combination of a "namespace" and a "key", and has
    /// a string value. These are defined as follows:
    /// </para>
    /// <list type="bullet">
    /// <item><description> Both the namespace and the key are non-null and non-empty strings.
    /// </description></item>
    /// <item><description> Both the namespace and the key contain only alphanumeric characters,
    /// hyphens, and underscores. </description></item>
    /// <item><description> The namespace always starts with "LaunchDarkly". </description></item>
    /// <item><description> The value can be any non-null string, including an empty string.
    /// </description></item>
    /// </list>
    /// <para>
    /// Unlike server-side SDKs, the persistent data store in this SDK treats the entire set of flags
    /// for a given user as a single value which is written to the store all at once, rather than one
    /// value per flag. This is for two reasons:
    /// </para>
    /// <list type="bullet">
    /// <item> The SDK assumes that the persistent store cannot be written to by any other process,
    /// so it does not need to implement read-through behavior when getting individual flags, and can
    /// read flags only from the in-memory cache. It only needs to read the persistent store at
    /// startup time or when changing users, to get any last known data for all flags at once. </item>
    /// <item> On many platforms, reading or writing multiple separate keys may be inefficient or may
    /// not be possible to do atomically. </item>
    /// </list>
    /// <para>
    /// The SDK will also provide its own caching layer on top of the persistent data store; the data
    /// store implementation should not provide caching, but simply do every query or update that the
    /// SDK tells it to do.
    /// </para>
    /// <para>
    /// Implementations do not need to worry about thread-safety; the SDK will ensure that it only
    /// calls one store method at a time.
    /// </para>
    /// <para>
    /// Error handling is defined as follows: if any data store operation encounters an I/O error, or
    /// is otherwise unable to complete its task, it should throw an exception to make the SDK aware
    /// of this. The SDK will decide whether to log the exception.
    /// </para>
    /// </remarks>
    public interface IPersistentDataStore : IDisposable
    {
        /// <summary>
        /// Attempts to retrieve a string value from the store.
        /// </summary>
        /// <param name="storageNamespace">the namespace identifier</param>
        /// <param name="key">the unique key within that namespace</param>
        /// <returns>the value, or null if not found</returns>
        string GetValue(string storageNamespace, string key);

        /// <summary>
        /// Attempts to update or remove a string value in the store.
        /// </summary>
        /// <param name="storageNamespace">the namespace identifier</param>
        /// <param name="key">the unique key within that namespace</param>
        /// <param name="value">the new value, or null to remove the key</param>
        void SetValue(string storageNamespace, string key, string value);
    }
}
