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
