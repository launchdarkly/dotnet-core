using System;

namespace LaunchDarkly.Sdk.Client.PlatformSpecific
{
    internal static partial class BackgroundDetection
    {
        private static event EventHandler<BackgroundModeChangedEventArgs> _backgroundModeChanged;

        private static object _backgroundModeChangedHandlersLock = new object();

        private static bool HasHandlers => _backgroundModeChanged != null && _backgroundModeChanged.GetInvocationList().Length != 0;

        public static event EventHandler<BackgroundModeChangedEventArgs> BackgroundModeChanged
        {
            add
            {
                lock (_backgroundModeChangedHandlersLock)
                {
                    var hadHandlers = HasHandlers;
                    _backgroundModeChanged += value;
                    if (!hadHandlers)
                    {
                        PlatformStartListening();
                    }
                }
            }
            remove
            {
                lock (_backgroundModeChangedHandlersLock)
                {
                    var hadHandlers = HasHandlers;
                    _backgroundModeChanged -= value;
                    if (hadHandlers && !HasHandlers)
                    {
                        PlatformStopListening();
                    }
                }
            }
        }

        private static void UpdateBackgroundMode(bool isInBackground)
        {
            var args = new BackgroundModeChangedEventArgs(isInBackground);
            var handlers = _backgroundModeChanged;
            handlers?.Invoke(null, args);
        }
    }

    internal class BackgroundModeChangedEventArgs
    {
        public bool IsInBackground { get; private set; }

        public BackgroundModeChangedEventArgs(bool isInBackground)
        {
            IsInBackground = isInBackground;
        }
    }
}
