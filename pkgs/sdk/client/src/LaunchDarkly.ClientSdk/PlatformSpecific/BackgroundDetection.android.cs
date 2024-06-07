using System;
using Android.App;
using Android.OS;

namespace LaunchDarkly.Sdk.Client.PlatformSpecific
{
    internal static partial class BackgroundDetection
    {
        private static ActivityLifecycleCallbacks _callbacks;
        private static Application _application;

        private static void PlatformStartListening()
        {
            _callbacks = new ActivityLifecycleCallbacks();
            _application = (Application)Application.Context;
            _application.RegisterActivityLifecycleCallbacks(_callbacks);
        }

        private static void PlatformStopListening()
        {
            _callbacks = null;
            _application = null;
        }

        private class ActivityLifecycleCallbacks : Java.Lang.Object, Application.IActivityLifecycleCallbacks
        {
            public void OnActivityCreated(Activity activity, Bundle savedInstanceState)
            {
            }

            public void OnActivityDestroyed(Activity activity)
            {
            }

            public void OnActivityPaused(Activity activity)
            {
                UpdateBackgroundMode(true);
            }

            public void OnActivityResumed(Activity activity)
            {
                UpdateBackgroundMode(false);
            }

            public void OnActivitySaveInstanceState(Activity activity, Bundle outState)
            {
            }

            public void OnActivityStarted(Activity activity)
            {
            }

            public void OnActivityStopped(Activity activity)
            {
            }
        }
    }
}
