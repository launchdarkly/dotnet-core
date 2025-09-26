using System.Text.RegularExpressions;

namespace LaunchDarkly.Sdk.Helpers
{

    /// <summary>
    /// Collection of utility functions for doing validation related work.
    /// </summary>
    public static class ValidationUtils
    {
        private static readonly Regex ValidCharsRegex = new Regex("^[-a-zA-Z0-9._]+\\z");

        /// <summary>
        /// Validates that a string does not contain invalid characters or exceed the max length of 8192 characters.
        /// </summary>
        /// <param name="sdkKey">the SDK key to validate.</param>
        /// <returns>Null if the input is valid, otherwise an error string describing the issue.</returns>
        public static string ValidateSdkKeyFormat(string sdkKey)
        {

            // For offline mode, we allow a null or empty SDK key and it is not invalid.
            if (string.IsNullOrEmpty(sdkKey))
            {
                return null;
            }

            if (sdkKey.Length > 8192)
            {
                return "SDK key cannot be longer than 1024 characters.";
            }

            if (!ValidCharsRegex.IsMatch(sdkKey))
            {
                return "SDK key contains invalid characters.";
            }

            return null;
        }

        /// <summary>
        /// Validates that a string is non-empty, not too longer for our systems, and only contains
        /// alphanumeric characters, hyphens, periods, and underscores.
        /// </summary>
        /// <param name="s">the string to validate.</param>
        /// <returns>Null if the input is valid, otherwise an error string describing the issue.</returns>
        public static string ValidateStringValue(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return "Empty string.";
            }

            if (s.Length > 64)
            {
                return "Longer than 64 characters.";
            }

            if (!ValidCharsRegex.IsMatch(s))
            {
                return "Contains invalid characters.";
            }

            return null;
        }

        /// <returns>A string with all spaces replaced by hyphens.</returns>
        public static string SanitizeSpaces(string s)
        {
            return s.Replace(" ", "-");
        }
    }
}
