using System;
using CoreFoundation;
using LaunchDarkly.Logging;

namespace LaunchDarkly.Sdk.Client.PlatformSpecific
{
    internal static partial class Logging
    {
        internal static ILogAdapter PlatformDefaultAdapter =>
            IOsLogAdapter.Instance;

        // Implementation of the LaunchDarkly.Logging API for sending output to iOS's standard
        // logging framework, OSLog.
        //
        // OSLog uses logger names slightly differently: it has two name-like properties, "subsystem"
        // and "category", with "category" being the more specific one. We're handling this by
        // splitting up our logger name as described in the docs for LoggingConfigurationBuilder.
        private sealed class IOsLogAdapter : ILogAdapter
        {
            internal static readonly IOsLogAdapter Instance =
                new IOsLogAdapter();

            public IChannel NewChannel(string name) => new ChannelImpl(name);

            private sealed class ChannelImpl : IChannel
            {
                private readonly CoreFoundation.OSLog _log;

                internal ChannelImpl(string name)
                {
                    string subsystem, category;
                    int pos = name.IndexOf('.');
                    if (pos > 0)
                    {
                        subsystem = name.Substring(0, pos);
                        category = name.Substring(pos + 1);
                    } else
                    {
                        subsystem = name;
                        category = "";
                    }
                    _log = new CoreFoundation.OSLog(subsystem, category);
                }

                // As defined in IChannel, IsEnabled really means "is it *potentially*
                // enabled" - it's a shortcut to make it easier to skip computing any
                // debug-level output if we know for sure that debug is disabled. But
                // we don't have a way to find that out here.
                public bool IsEnabled(LogLevel level) => true;

                private void LogString(LogLevel level, string s)
                {
                    switch (level)
                    {
                        case LogLevel.Debug:
                            _log.Log(OSLogLevel.Debug, s);
                            break;
                        case LogLevel.Info:
                            _log.Log(OSLogLevel.Info, s);
                            break;
                        case LogLevel.Warn:
                            _log.Log(OSLogLevel.Default, s);
                            break;
                        case LogLevel.Error:
                            _log.Log(OSLogLevel.Error, s);
                            break;
                    }
                }

                public void Log(LogLevel level, object message) =>
                    LogString(level, message.ToString());

                public void Log(LogLevel level, string format, object param) =>
                    LogString(level, string.Format(format, param));

                public void Log(LogLevel level, string format, object param1, object param2) =>
                    LogString(level, string.Format(format, param1, param2));

                public void Log(LogLevel level, string format, params object[] allParams) =>
                    LogString(level, string.Format(format, allParams));
            }
        }
    }
}
