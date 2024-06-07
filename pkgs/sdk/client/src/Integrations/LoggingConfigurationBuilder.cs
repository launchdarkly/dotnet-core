using System;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Client.Subsystems;

namespace LaunchDarkly.Sdk.Client.Integrations
{
    /// <summary>
    /// Contains methods for configuring the SDK's logging behavior.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If you want to set non-default values for any of these properties, create a builder with
    /// <see cref="Components.Logging()"/>, change its properties with the methods of this class, and pass it
    /// to <see cref="ConfigurationBuilder.Logging(LoggingConfigurationBuilder)" />.
    /// </para>
    /// <para>
    /// The default behavior, if you do not change any properties, depends on the runtime platform:
    /// </para>
    /// <list type="bullet">
    /// <item> On Android, it uses
    /// <a href="https://developer.android.com/reference/android/util/Log"><c>android.util.Log</c></a>. </item>
    /// <item> On iOS, it uses the Apple
    /// <a href="https://developer.apple.com/documentation/os/logging">unified logging system</a>
    /// (<c>OSLog</c>). </item>
    /// <item> On all other platforms, it writes to <see cref="Console.Error"/>. </item>
    /// </list>
    /// <para>
    /// By default, the minimum log level is <c>Info</c> (that is, <c>Debug</c> logging is
    /// disabled). This can be overridden with <see cref="LoggingConfigurationBuilder.Level(LogLevel)"/>.
    /// </para>
    /// <para>
    /// The base logger name is normally <c>LaunchDarkly.Sdk</c> (on iOS, this corresponds to a
    /// "subsystem" name of "LaunchDarkly" and a "category" name of "Sdk"). See <see cref="BaseLoggerName(string)"/>
    /// for more about logger names and how to change the name.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    ///     var config = Configuration.Builder("my-sdk-key")
    ///         .Logging(Components.Logging().Level(LogLevel.Warn))
    ///         .Build();
    /// </code>
    /// </example>
    public sealed class LoggingConfigurationBuilder
    {
        private string _baseLoggerName = null;
        private ILogAdapter _logAdapter = null;
        private LogLevel? _minimumLevel = null;

        /// <summary>
        /// Creates a new builder with default properties.
        /// </summary>
        public LoggingConfigurationBuilder() { }

        /// <summary>
        /// Specifies a custom base logger name.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Logger names are used to give context to the log output, indicating that it is from the
        /// LaunchDarkly SDK instead of another component, or indicating a more specific area of
        /// functionality within the SDK. The default console logging implementation shows the logger
        /// name in brackets, for instance:
        /// </para>
        /// <code>
        ///     [LaunchDarkly.Sdk.DataSource] INFO: Reconnected to LaunchDarkly stream
        /// </code>
        /// <para>
        /// If you are using an adapter for a third-party logging framework (see
        /// <see cref="Adapter(ILogAdapter)"/>), most frameworks have a mechanism for filtering log
        /// output by the logger name.
        /// </para>
        /// <para>
        /// By default, the SDK uses a base logger name of <c>LaunchDarkly.Sdk</c>. Messages will be
        /// logged either under this name, or with a suffix to indicate what general area of
        /// functionality is involved:
        /// </para>
        /// <list type="bullet">
        /// <item> <c>.DataSource</c>: problems or status messages regarding how the SDK gets
        /// feature flag data from LaunchDarkly. </item>
        /// <item> <c>.DataStore</c>: problems or status messages regarding how the SDK stores its
        /// feature flag data. </item>
        /// <item> <c>.Events</c> problems or status messages regarding the SDK's delivery of
        /// analytics event data to LaunchDarkly. </item>
        /// </list>
        /// <para>
        /// Setting <c>BaseLoggerName</c> to a non-null value overrides the default. The SDK still
        /// adds the same suffixes to the name, so for instance if you set it to <c>"LD"</c>, the
        /// example message above would show <c>[LD.DataSource]</c>.
        /// </para>
        /// <para>
        /// When using the default logging framework in iOS, logger names are handled slightly
        /// differently because iOS's <c>OSLog</c> has two kinds of logger names: a general
        /// "subsystem", and a more specific "category". The SDK handles this by taking everything
        /// after the first period in the logger name as the category: for instance, for
        /// <c>LaunchDarkly.Sdk.DataSource</c>, the subsystem is <c>LaunchDarkly</c> and the category
        /// is <c>Sdk.DataSource</c>. If you set a custom base logger name, the same rules apply, so
        /// for instance if you set it to <c>"LD"</c> then then <c>LD.DataSource</c> would become
        /// a subsystem of <c>LD</c> and a category of <c>DataSource</c>.
        /// </para>
        /// </remarks>
        /// <returns>the same builder</returns>
        public LoggingConfigurationBuilder BaseLoggerName(string baseLoggerName)
        {
            _baseLoggerName = baseLoggerName;
            return this;
        }

        /// <summary>
        /// Specifies the implementation of logging to use.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The <a href="https://github.com/launchdarkly/dotnet-logging"><c>LaunchDarkly.Logging</c></a> API defines the
        /// <c>ILogAdapter</c> interface to specify where log output should be sent. By default, it is set to
        /// <c>Logs.ToConsole</c>, meaning that output will be sent to <c>Console.Error</c>. You may use other
        /// <c>LaunchDarkly.Logging.Logs</c> methods, or a custom implementation, to handle log output differently.
        /// <c>Logs.None</c> disables logging (equivalent to <see cref="Components.NoLogging"/>).
        /// </para>
        /// <para>
        /// For more about logging adapters, see the <a href="https://launchdarkly.github.io/dotnet-logging">API
        /// documentation</a> for <c>LaunchDarkly.Logging</c>.
        /// </para>
        /// <para>
        /// If you don't need to customize any options other than the adapter, you can call
        /// <see cref="Components.Logging(ILogAdapter)"/> as a shortcut rather than using
        /// <see cref="LoggingConfigurationBuilder"/>.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        ///     // This example configures the SDK to send log output to a file writer.
        ///     var writer = File.CreateText("sdk.log");
        ///     var config = Configuration.Builder("my-sdk-key")
        ///         .Logging(Components.Logging().Adapter(Logs.ToWriter(writer)))
        ///         .Build();
        /// </code>
        /// </example>
        /// <param name="adapter">an <c>ILogAdapter</c> for the desired logging implementation;
        /// <see langword="null"/> to use the default implementation</param>
        /// <returns>the same builder</returns>
        public LoggingConfigurationBuilder Adapter(ILogAdapter adapter)
        {
            _logAdapter = adapter;
            return this;
        }

        /// <summary>
        /// Specifies the lowest level of logging to enable.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This adds a log level filter that is applied regardless of what implementation of logging is
        /// being used, so that log messages at lower levels are suppressed. For instance, setting the
        /// minimum level to <see cref="LogLevel.Info"/> means that <c>Debug</c>-level output is disabled.
        /// External logging frameworks may also have their own mechanisms for setting a minimum log level.
        /// </para>
        /// <para>
        /// If you did not specify an <see cref="ILogAdapter"/> at all, so it is using the default <c>Console.Error</c>
        /// destination, then the default minimum logging level is <c>Info</c>.
        /// </para>
        /// <para>
        /// If you did specify an <see cref="ILogAdapter"/>, then the SDK does not apply a level filter by
        /// default. This is so as not to interfere with any other configuration that you may have set up
        /// in an external logging framework. However, you can still use this method to set a higher level
        /// so that any messages below that level will not be sent to the external framework at all.
        /// </para>
        /// </remarks>
        /// <param name="minimumLevel">the lowest level of logging to enable</param>
        /// <returns>the same builder</returns>
        public LoggingConfigurationBuilder Level(LogLevel minimumLevel)
        {
            _minimumLevel = minimumLevel;
            return this;
        }

        /// <summary>
        /// Called internally by the SDK to create a configuration instance. Applications do not need
        /// to call this method.
        /// </summary>
        /// <returns>the logging configuration</returns>
        public LoggingConfiguration CreateLoggingConfiguration()
        {
            ILogAdapter logAdapter;
            if (_logAdapter is null)
            {
                logAdapter = PlatformSpecific.Logging.DefaultAdapter
                    .Level(_minimumLevel ?? LogLevel.Info);
            }
            else
            {
                logAdapter = _minimumLevel.HasValue ?
                   _logAdapter.Level(_minimumLevel.Value) :
                   _logAdapter;
            }
            return new LoggingConfiguration(
                _baseLoggerName,
                logAdapter
                );
        }
    }
}
