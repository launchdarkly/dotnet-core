
namespace LaunchDarkly.Sdk.Internal.Http
{
    /// <summary>
    /// Helper methods to provide standardized HTTP error-handling behavior in the SDKs.
    /// </summary>
    public static class HttpErrors
    {
        /// <summary>
        /// Returns true if this type of error could be expected to eventually resolve itself,
        /// or false if it indicates a configuration problem or client logic error such that the
        /// client should give up on making any further requests.
        /// </summary>
        /// <param name="status">a status code</param>
        /// <returns>true if retrying is appropriate</returns>
        public static bool IsRecoverable(int status)
        {
            if (status >= 400 && status <= 499)
            {
                return (status == 400) || (status == 408) || (status == 429);
            }
            return true;
        }

        public static string ErrorMessage(int status, string context, string recoverableMessage) =>
            string.Format("{0} for {1} - {2}",
                ErrorMessageBase(status),
                context,
                IsRecoverable(status) ? recoverableMessage : "giving up permanently"
                );

        public static string ErrorMessageBase(int status) =>
            string.Format("HTTP error {0}{1}",
                status,
               (status == 401 || status == 403) ? " (invalid SDK key)" : "");
    }
}
