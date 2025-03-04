namespace LaunchDarkly.Sdk.Server.Internal
{
    /// <summary>
    /// Constants for headers returned from, or sent to, LaunchDarkly.
    /// <para>
    /// All header keys should be lowercase.
    /// </para>
    /// </summary>
    public static class HeaderConstants
    {
        /// <summary>
        /// The LaunchDarkly environment ID. This is included in responses from streaming and polling endpoints.
        /// </summary>
        public static string EnvironmentId = "x-ld-envid";
    }
}
