using System;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Helpers;

namespace LaunchDarkly.Sdk
{

    /// <summary>
    /// Contains methods for configuring the application metadata.  Application metadata may be used in LaunchDarkly
    /// analytics or other product features.
    /// </summary>
    public sealed class ApplicationInfoBuilder
    {
        private string _applicationId;

        private string _applicationName;

        private string _applicationVersion;

        private string _applicationVersionName;

        private readonly Logger _logger = Logs.Default.Logger(nameof(ApplicationInfoBuilder));

        /// <returns>a new <see cref="ApplicationInfo"/> from the current build properties.</returns>
        public ApplicationInfo Build()
        {
            return new ApplicationInfo(_applicationId, _applicationName, _applicationVersion, _applicationVersionName);
        }

        /// <summary>
        /// Sets a unique identifier representing the application where the LaunchDarkly SDK is running.
        /// This can be specified as any string value as long as it only uses the following characters: ASCII
        /// letters, ASCII digits, period, hyphen, underscore. A string containing any other characters will be
        /// ignored.
        /// </summary>
        /// <param name="applicationId">the application identifier</param>
        /// <returns>the builder</returns>
        public ApplicationInfoBuilder ApplicationId(string applicationId)
        {
            ValidatedThenSet("ApplicationId", s => _applicationId = s, applicationId, _logger);
            return this;
        }

        /// <summary>
        /// Sets a human friendly name for the application in which the LaunchDarkly SDK is running.
        ///
        /// This can be specified as any string value as long as it only uses the following characters: ASCII
        /// letters, ASCII digits, period, hyphen, underscore. A string containing any other characters will be
        /// ignored.
        /// </summary>
        /// <param name="applicationName">the human friendly name</param>
        /// <returns>the builder</returns>
        public ApplicationInfoBuilder ApplicationName(string applicationName)
        {
            ValidatedThenSet("ApplicationName", s => _applicationName = s, applicationName, _logger);
            return this;
        }

        /// <summary>
        /// Sets a unique identifier representing the version of the application where the LaunchDarkly SDK
        /// is running.
        ///
        /// This can be specified as any string value as long as it only uses the following characters: ASCII
        /// letters, ASCII digits, period, hyphen, underscore. A string containing any other characters will be
        /// ignored.
        /// </summary>
        /// <param name="applicationVersion">the application version</param>
        /// <returns>the builder</returns>
        public ApplicationInfoBuilder ApplicationVersion(string applicationVersion)
        {
            ValidatedThenSet("ApplicationVersion", s => _applicationVersion = s, applicationVersion, _logger);
            return this;
        }

        /// <summary>
        /// Sets a human friendly name for the version of the application in which the LaunchDarkly SDK is running.
        ///
        /// This can be specified as any string value as long as it only uses the following characters: ASCII
        /// letters, ASCII digits, period, hyphen, underscore. A string containing any other characters will be
        /// ignored.
        /// </summary>
        /// <param name="applicationVersionName">the human friendly version name</param>
        /// <returns>the builder</returns>
        public ApplicationInfoBuilder ApplicationVersionName(string applicationVersionName)
        {
            ValidatedThenSet("ApplicationVersionName", s => _applicationVersionName = s, applicationVersionName, _logger);
            return this;
        }

        /// <summary>
        /// Validates the input and then invokes the property setter
        /// </summary>
        /// <param name="propertyName">name of the property trying to be set</param>
        /// <param name="propertySetter">to be invoked when validation succeeds</param>
        /// <param name="input">the input to validate and then use if valid</param>
        /// <param name="logger">logger for logging validation errors</param>
        private static void ValidatedThenSet(String propertyName, Action<string> propertySetter, string input, Logger logger)
        {
            if (input == null)
            {
                propertySetter(null);
                return;
            }

            var sanitized = ValidationUtils.SanitizeSpaces(input);
            var error = ValidationUtils.ValidateStringValue(sanitized);
            if (error != null)
            {
                // intentionally ignore invalid values
                logger.Warn("Issue setting {0} to value '{1}'. {2}", propertyName, sanitized, error);
                return;
            }

            propertySetter(sanitized);
        }
    }
}
