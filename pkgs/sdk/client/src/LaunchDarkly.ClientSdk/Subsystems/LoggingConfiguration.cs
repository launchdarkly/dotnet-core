using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Client.Integrations;

namespace LaunchDarkly.Sdk.Client.Subsystems
{
    /// <summary>
    /// Encapsulates the SDK's general logging configuration.
    /// </summary>
    public sealed class LoggingConfiguration
    {
        /// <summary>
        /// The configured base logger name, or <c>null</c> to use the default.
        /// </summary>
        /// <seealso cref="LoggingConfigurationBuilder.BaseLoggerName(string)"/>
        public string BaseLoggerName { get; }

        /// <summary>
        /// The implementation of logging that the SDK will use.
        /// </summary>
        /// <seealso cref="LoggingConfigurationBuilder.Adapter(ILogAdapter)"/>
        public ILogAdapter LogAdapter { get; }

        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        /// <param name="baseLoggerName">value for <see cref="BaseLoggerName"/></param>
        /// <param name="logAdapter">value for <see cref="LogAdapter"/></param>
        public LoggingConfiguration(
            string baseLoggerName,
            ILogAdapter logAdapter
            )
        {
            BaseLoggerName = baseLoggerName;
            LogAdapter = logAdapter ?? Logs.None;
        }
    }
}
