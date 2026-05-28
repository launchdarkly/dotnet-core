namespace LaunchDarkly.Sdk.Server.Subsystems
{
    /// <summary>
    /// Optional interface for data stores that can disable their internal cache.
    /// </summary>
    /// <remarks>
    /// This is currently for internal implementations only.
    /// </remarks>
    internal interface IDisableableCache
    {
        /// <summary>
        /// Disables the internal cache. After this call, the cache is no longer
        /// consulted on reads and no longer populated by writes.
        /// </summary>
        /// <remarks>
        /// Implementations should clear, dispose, and dereference cache instances
        /// so the memory can be reclaimed. The call must be idempotent: subsequent
        /// invocations should be safe and have no further effect.
        /// </remarks>
        void DisableCache();
    }
}
