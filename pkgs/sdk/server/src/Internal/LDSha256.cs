using System.Security.Cryptography;

namespace LaunchDarkly.Sdk.Server.Internal
{
    // Hasher which uses HashData on .NET 5.0+ platforms and ComputeHash on older platforms.
    // HashData is static and thread-safe offering better performance when available.
#if NET5_0_OR_GREATER
    /// <summary>
    /// Thread-safe hasher using SHA256.HashData for .NET 5.0+ platforms.
    /// </summary>
    internal static class LdSha256
    {
        public static byte[] HashData(byte[] data) {
            return SHA256.HashData(data);
        }
    }
#else
    /// <summary>
    /// Thread-safe hasher using SHA256.ComputeHash for .NET Framework and .netstandard targets.
    /// </summary>
    /// <remarks>
    /// This hasher creates a SHA256 instance per-call. This is likely to perform better under high parallelism
    /// than locking. With low parallelism, locking would have lower overhead and exert lower pressure on the GC.
    /// The pre-existing evaluation algorithm was already using per-call SHA1 instances, so this should have
    /// reasonable performance characteristics.
    /// </remarks>
    internal static class LdSha256
    {
        public static byte[] HashData(byte[] data)
        {
            using (var hasher = SHA256.Create())
            {
                return hasher.ComputeHash(data);
            }
        }
    }
#endif
}
