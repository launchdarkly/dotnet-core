using System;
using LaunchDarkly.Logging;
using AndroidLog = Android.Util.Log;

namespace LaunchDarkly.Sdk.Client.PlatformSpecific
{
    internal static partial class Logging
    {
        internal static ILogAdapter PlatformDefaultAdapter =>
            AndroidLogAdapter.Instance;

        // Implementation of the LaunchDarkly.Logging API for sending output to Android's standard
        // logging framework. Notes:
        // 1. This sets the log tag to be the same as the LaunchDarkly logger name (see LogNames and
        //    LoggingConfigurationBuilder.BaseLoggerName).
        // 2. The underlying Android API is a little different: Log has methods called "d", "i", "w",
        //    and "e" rather than Debug, Info, Warn, and Error, and they always take a message string
        //    rather than a format string plus variables. MAUI's Android layer adds some decoration of
        //    its own so that we can use .NET-style format strings.
        private sealed class AndroidLogAdapter : ILogAdapter
        {
            internal static readonly AndroidLogAdapter Instance =
                new AndroidLogAdapter();

            public IChannel NewChannel(string name) => new ChannelImpl(name);

            private sealed class ChannelImpl : IChannel
            {
                private readonly string _name;

                internal ChannelImpl(string name)
                {
                    _name = name;
                }

                // As defined in IChannel, IsEnabled really means "is it *potentially*
                // enabled" - it's a shortcut to make it easier to skip computing any
                // debug-level output if we know for sure that debug is disabled. But
                // we don't have a way to find that out here.
                public bool IsEnabled(LogLevel level) => true;

                public void Log(LogLevel level, object message)
                {
                    var s = message.ToString();
                    switch (level)
                    {
                        case LogLevel.Debug:
                            AndroidLog.Debug(_name, s);
                            break;
                        case LogLevel.Info:
                            AndroidLog.Info(_name, s);
                            break;
                        case LogLevel.Warn:
                            AndroidLog.Warn(_name, s);
                            break;
                        case LogLevel.Error:
                            AndroidLog.Error(_name, s);
                            break;
                    }
                }

                public void Log(LogLevel level, string format, object param)
                {
                    switch (level)
                    {
                        case LogLevel.Debug:
                            AndroidLog.Debug(_name, format, param);
                            break;
                        case LogLevel.Info:
                            AndroidLog.Info(_name, format, param);
                            break;
                        case LogLevel.Warn:
                            AndroidLog.Warn(_name, format, param);
                            break;
                        case LogLevel.Error:
                            AndroidLog.Error(_name, format, param);
                            break;
                    }
                }

                public void Log(LogLevel level, string format, object param1, object param2)
                {
                    switch (level)
                    {
                        case LogLevel.Debug:
                            AndroidLog.Debug(_name, format, param1, param2);
                            break;
                        case LogLevel.Info:
                            AndroidLog.Info(_name, format, param1, param2);
                            break;
                        case LogLevel.Warn:
                            AndroidLog.Warn(_name, format, param1, param2);
                            break;
                        case LogLevel.Error:
                            AndroidLog.Error(_name, format, param1, param2);
                            break;
                    }
                }

                public void Log(LogLevel level, string format, params object[] allParams)
                {
                    switch (level)
                    {
                        case LogLevel.Debug:
                            AndroidLog.Debug(_name, format, allParams);
                            break;
                        case LogLevel.Info:
                            AndroidLog.Info(_name, format, allParams);
                            break;
                        case LogLevel.Warn:
                            AndroidLog.Warn(_name, format, allParams);
                            break;
                        case LogLevel.Error:
                            AndroidLog.Error(_name, format, allParams);
                            break;
                    }
                }
            }
        }
    }
}
