using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Client.Integrations;

namespace LaunchDarkly.Sdk.Client.Interfaces
{
    /// <summary>
    /// Interface for the standard SDK client methods and properties. The only implementation of this is <see cref="LdClient"/>.
    /// </summary>
    /// <remarks>
    /// See also <see cref="ILdClientExtensions"/>, which provides convenience methods that build upon
    /// this interface.
    /// </remarks>
    public interface ILdClient : IDisposable
    {
        /// <summary>
        /// A mechanism for tracking the status of the data source.
        /// </summary>
        /// <remarks>
        /// The data source is the mechanism that the SDK uses to get feature flag configurations, such as a
        /// streaming connection (the default) or poll requests. The <see cref="IDataSourceStatusProvider"/>
        /// has methods for checking whether the data source is (as far as the SDK knows) currently operational,
        /// and tracking changes in this status. This property will never be null.
        /// </remarks>
        IDataSourceStatusProvider DataSourceStatusProvider { get; }

        /// <summary>
        /// A mechanism for tracking changes in feature flag configurations.
        /// </summary>
        /// <remarks>
        /// The <see cref="IFlagTracker"/> contains methods for requesting notifications about feature flag
        /// changes using an event listener model.
        /// </remarks>
        IFlagTracker FlagTracker { get; }

        /// <summary>
        /// Returns a boolean value indicating LaunchDarkly connection and flag state within the client.
        /// </summary>
        /// <remarks>
        /// <para>
        /// When you first start the client, once <see cref="LdClient.Init(Configuration, Context, TimeSpan)"/> or
        /// <see cref="LdClient.InitAsync(Configuration, Context)"/> has returned, <see cref="Initialized"/> should be
        /// <see langword="true"/> if and only if either 1. it connected to LaunchDarkly and successfully retrieved
        /// flags, or 2. it started in offline mode so there's no need to connect to LaunchDarkly. If the client
        /// timed out trying to connect to LD, then <see cref="Initialized"/> is <see langword="false"/> (even if we
        /// do have cached flags).  If the client connected and got a 401 error, <see cref="Initialized"/> is
        /// <see langword="false"/>. This serves the purpose of letting the app know that there was a problem of some kind.
        /// </para>
        /// <para>
        /// If you call <see cref="Identify(Context, TimeSpan)"/> or <see cref="IdentifyAsync(Context)"/>,
        /// <see cref="Initialized"/> will become <see langword="false"/> until the SDK receives the new context's flags.
        /// </para>
        /// </remarks>
        bool Initialized { get; }

        /// <summary>
        /// Indicates whether the SDK is configured to be always offline.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is initially <see langword="true"/> if you set it to <see langword="true"/> in the configuration with
        /// <see cref="ConfigurationBuilder.Offline(bool)"/>. However, you can change it at any time to allow the client
        /// to go online, or force it to go offline, using <see cref="SetOffline(bool, TimeSpan)"/> or
        /// <see cref="SetOfflineAsync(bool)"/>.
        /// </para>
        /// <para>
        /// When <see cref="Offline"/> is <see langword="false"/>, the SDK connects to LaunchDarkly if possible, but
        /// this does not guarantee that the connection is successful. There is currently no mechanism to detect whether
        /// the SDK is currently connected to LaunchDarkly.
        /// </para>
        /// </remarks>
        bool Offline { get; }

        /// <summary>
        /// Sets whether the SDK should be always offline.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is equivalent to <see cref="SetOfflineAsync(bool)"/>, but as a synchronous method.
        /// </para>
        /// <para>
        /// If you set the property to <see langword="true"/>, any existing connection will be dropped, and the
        /// method immediately returns <see langword="false"/>.
        /// </para>
        /// <para>
        /// If you set it to <see langword="false"/> when it was previously <see langword="true"/>, but no connection can
        /// be made because the network is not available, the method immediately returns <see langword="false"/>, but the
        /// SDK will attempt to connect later if the network becomes available.
        /// </para>
        /// <para>
        /// If you set it to <see langword="false"/> when it was previously <see langword="true"/>, and the network is
        /// available, the SDK will attempt to connect to LaunchDarkly. If the connection succeeds within the interval
        /// <c>maxWaitTime</c>, the method returns <see langword="true"/>. If the connection permanently fails (e.g. if
        /// the mobile key is invalid), the method returns <see langword="false"/>. If the connection attempt is still in
        /// progress after <c>maxWaitTime</c> elapses, the method returns <see langword="false"/>, but the connection
        /// might succeed later.
        /// </para>
        /// </remarks>
        /// <param name="value">true if the client should be always offline</param>
        /// <param name="maxWaitTime">the maximum length of time to wait for a connection</param>
        /// <returns>true if a new connection was successfully made</returns>
        bool SetOffline(bool value, TimeSpan maxWaitTime);

        /// <summary>
        /// Sets whether the SDK should be always offline.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is equivalent to <see cref="SetOffline(bool, TimeSpan)"/>, but as an asynchronous method.
        /// </para>
        /// <para>
        /// If you set the property to <see langword="true"/>, any existing connection will be dropped, and the
        /// task immediately yields <see langword="false"/>.
        /// </para>
        /// <para>
        /// If you set it to <see langword="false"/> when it was previously <see langword="true"/>, but no connection can
        /// be made because the network is not available, the task immediately yields <see langword="false"/>, but the
        /// SDK will attempt to connect later if the network becomes available.
        /// </para>
        /// <para>
        /// If you set it to <see langword="false"/> when it was previously <see langword="true"/>, and the network is
        /// available, the SDK will attempt to connect to LaunchDarkly. If and when the connection succeeds, the task
        /// yields <see langword="true"/>. If and when the connection permanently fails (e.g. if the mobile key is
        /// invalid), the task yields <see langword="false"/>.
        /// </para>
        /// </remarks>
        /// <param name="value">true if the client should be always offline</param>
        /// <returns>a task that yields true if a new connection was successfully made</returns>
        Task SetOfflineAsync(bool value);

        /// <summary>
        /// Returns the boolean value of a feature flag for a given flag key.
        /// </summary>
        /// <param name="key">the unique feature key for the feature flag</param>
        /// <param name="defaultValue">the default value of the flag</param>
        /// <returns>the variation for the selected user, or <c>defaultValue</c> if the flag is
        /// disabled in the LaunchDarkly control panel</returns>
        bool BoolVariation(string key, bool defaultValue = false);

        /// <summary>
        /// Returns the boolean value of a feature flag for a given flag key, in an object that also
        /// describes the way the value was determined.
        /// </summary>
        /// <remarks>
        /// The <see cref="EvaluationDetail{T}.Reason"/> property in the result will also be included in analytics
        /// events, if you are capturing detailed event data for this flag.
        /// </remarks>
        /// <param name="key">the unique feature key for the feature flag</param>
        /// <param name="defaultValue">the default value of the flag</param>
        /// <returns>an <c>EvaluationDetail</c> object</returns>
        EvaluationDetail<bool> BoolVariationDetail(string key, bool defaultValue = false);

        /// <summary>
        /// Returns the string value of a feature flag for a given flag key.
        /// </summary>
        /// <param name="key">the unique feature key for the feature flag</param>
        /// <param name="defaultValue">the default value of the flag</param>
        /// <returns>the variation for the selected user, or <c>defaultValue</c> if the flag is
        /// disabled in the LaunchDarkly control panel</returns>
        string StringVariation(string key, string defaultValue);

        /// <summary>
        /// Returns the string value of a feature flag for a given flag key, in an object that also
        /// describes the way the value was determined.
        /// </summary>
        /// <remarks>
        /// The <see cref="EvaluationDetail{T}.Reason"/> property in the result will also be included in analytics
        /// events, if you are capturing detailed event data for this flag.
        /// </remarks>
        /// <param name="key">the unique feature key for the feature flag</param>
        /// <param name="defaultValue">the default value of the flag</param>
        /// <returns>an <c>EvaluationDetail</c> object</returns>
        EvaluationDetail<string> StringVariationDetail(string key, string defaultValue);

        /// <summary>
        /// Returns the single-precision floating-point value of a feature flag for a given flag key.
        /// </summary>
        /// <param name="key">the unique feature key for the feature flag</param>
        /// <param name="defaultValue">the default value of the flag</param>
        /// <returns>the variation for the selected user, or <c>defaultValue</c> if the flag is
        /// disabled in the LaunchDarkly control panel</returns>
        float FloatVariation(string key, float defaultValue = 0);

        /// <summary>
        /// Returns the single-precision floating-point value of a feature flag for a given flag key,
        /// in an object that also describes the way the value was determined.
        /// </summary>
        /// <remarks>
        /// The <see cref="EvaluationDetail{T}.Reason"/> property in the result will also be included in analytics
        /// events, if you are capturing detailed event data for this flag.
        /// </remarks>
        /// <param name="key">the unique feature key for the feature flag</param>
        /// <param name="defaultValue">the default value of the flag</param>
        /// <returns>an <c>EvaluationDetail</c> object</returns>
        EvaluationDetail<float> FloatVariationDetail(string key, float defaultValue = 0);

        /// <summary>
        /// Returns the double-precision floating-point value of a feature flag for a given flag key.
        /// </summary>
        /// <param name="key">the unique feature key for the feature flag</param>
        /// <param name="defaultValue">the default value of the flag</param>
        /// <returns>the variation for the selected user, or <c>defaultValue</c> if the flag is
        /// disabled in the LaunchDarkly control panel</returns>
        double DoubleVariation(string key, double defaultValue = 0);

        /// <summary>
        /// Returns the double-precision floating-point value of a feature flag for a given flag key,
        /// in an object that also describes the way the value was determined.
        /// </summary>
        /// <remarks>
        /// The <see cref="EvaluationDetail{T}.Reason"/> property in the result will also be included in analytics
        /// events, if you are capturing detailed event data for this flag.
        /// </remarks>
        /// <param name="key">the unique feature key for the feature flag</param>
        /// <param name="defaultValue">the default value of the flag</param>
        /// <returns>an <c>EvaluationDetail</c> object</returns>
        EvaluationDetail<double> DoubleVariationDetail(string key, double defaultValue = 0);

        /// <summary>
        /// Returns the integer value of a feature flag for a given flag key.
        /// </summary>
        /// <param name="key">the unique feature key for the feature flag</param>
        /// <param name="defaultValue">the default value of the flag</param>
        /// <returns>the variation for the selected user, or <c>defaultValue</c> if the flag is
        /// disabled in the LaunchDarkly control panel</returns>
        int IntVariation(string key, int defaultValue = 0);

        /// <summary>
        /// Returns the integer value of a feature flag for a given flag key, in an object that also
        /// describes the way the value was determined.
        /// </summary>
        /// <remarks>
        /// The <see cref="EvaluationDetail{T}.Reason"/> property in the result will also be included in analytics
        /// events, if you are capturing detailed event data for this flag.
        /// </remarks>
        /// <param name="key">the unique feature key for the feature flag</param>
        /// <param name="defaultValue">the default value of the flag</param>
        /// <returns>an <c>EvaluationDetail</c> object</returns>
        EvaluationDetail<int> IntVariationDetail(string key, int defaultValue = 0);

        /// <summary>
        /// Returns the JSON value of a feature flag for a given flag key.
        /// </summary>
        /// <param name="key">the unique feature key for the feature flag</param>
        /// <param name="defaultValue">the default value of the flag</param>
        /// <returns>the variation for the selected user, or <c>defaultValue</c> if the flag is
        /// disabled in the LaunchDarkly control panel</returns>
        LdValue JsonVariation(string key, LdValue defaultValue);

        /// <summary>
        /// Returns the JSON value of a feature flag for a given flag key, in an object that also
        /// describes the way the value was determined.
        /// </summary>
        /// <remarks>
        /// The <see cref="EvaluationDetail{T}.Reason"/> property in the result will also be included in analytics
        /// events, if you are capturing detailed event data for this flag.
        /// </remarks>
        /// <param name="key">the unique feature key for the feature flag</param>
        /// <param name="defaultValue">the default value of the flag</param>
        /// <returns>an <c>EvaluationDetail</c> object</returns>
        EvaluationDetail<LdValue> JsonVariationDetail(string key, LdValue defaultValue);

        /// <summary>
        /// Tracks that current user performed an event for the given event name.
        /// </summary>
        /// <param name="eventName">the name of the event</param>
        void Track(string eventName);

        /// <summary>
        /// Tracks that the current user performed an event for the given event name, with additional JSON data.
        /// </summary>
        /// <param name="eventName">the name of the event</param>
        /// <param name="data">a JSON value containing additional data associated with the event</param>
        void Track(string eventName, LdValue data);

        /// <summary>
        /// Tracks that the current user performed an event for the given event name, and associates it with a
        /// numeric metric value.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="eventName">the name of the event</param>
        /// <param name="data">a JSON value containing additional data associated with the event; pass
        /// <see cref="LdValue.Null"/> if you do not need this value</param>
        /// <param name="metricValue">this value is used by the LaunchDarkly experimentation feature in
        /// numeric custom metrics, and will also be returned as part of the custom event for Data Export</param>
        void Track(string eventName, LdValue data, double metricValue);

        /// <summary>
        /// Returns a map from feature flag keys to <see cref="LdValue"/> feature flag values for the current user.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If the result of a flag's value would have returned the default variation, the value in the map will contain
        /// <see cref="LdValue.Null"/>. If the client is offline or has not been initialized, an empty
        /// map will be returned.
        /// </para>
        /// <para>
        /// This method will not send analytics events back to LaunchDarkly.
        /// </para>
        /// </remarks>
        /// <returns>a map from feature flag keys to values for the current user</returns>
        IDictionary<string, LdValue> AllFlags();

        /// <summary>
        /// Changes the current evaluation context, requests flags for that context from LaunchDarkly if we are online,
        /// and generates an analytics event to tell LaunchDarkly about the context.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is equivalent to <see cref="IdentifyAsync(Context)"/>, but as a synchronous method.
        /// </para>
        /// <para>
        /// If the SDK is online, <see cref="Identify"/> waits to receive feature flag values for the new context from
        /// LaunchDarkly. If it receives the new flag values before <c>maxWaitTime</c> has elapsed, it returns
        /// <see langword="true"/>. If the timeout elapses, it returns <see langword="false"/> (although the SDK might
        /// still receive the flag values later). If we do not need to request flags from LaunchDarkly because we are
        /// in offline mode, it returns <see langword="true"/>.
        /// </para>
        /// <para>
        /// If you do not want to wait, you can either set <c>maxWaitTime</c> to zero or call <see cref="IdentifyAsync(Context)"/>.
        /// </para>
        /// </remarks>
        /// <param name="context">the new evaluation context; see <see cref="LdClient"/> for more
        /// about setting the context and optionally requesting a unique key for it</param>
        /// <param name="maxWaitTime">the maximum time to wait for the new flag values</param>
        /// <returns>true if new flag values were obtained</returns>
        /// <seealso cref="IdentifyAsync(Context)"/>
        bool Identify(Context context, TimeSpan maxWaitTime);

        /// <summary>
        /// Changes the current evaluation context, requests flags for that context from LaunchDarkly if we are online,
        /// and generates an analytics event to tell LaunchDarkly about the context.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is equivalent to <see cref="Identify(Context, TimeSpan)"/>, but as an asynchronous method.
        /// </para>
        /// <para>
        /// If the SDK is online, the returned task is completed once the SDK has received feature flag values for the
        /// new user from LaunchDarkly, or received an unrecoverable error; it yields <see langword="true"/> for success
        /// or <see langword="false"/> for an error. If the SDK is offline, the returned task is completed immediately
        /// and yields <see langword="true"/>.
        /// </para>
        /// </remarks>
        /// <param name="context">the new evaluation context; see <see cref="LdClient"/> for more
        /// about setting the context and optionally requesting a unique key for it</param>
        /// <returns>a task that yields true if new flag values were obtained</returns>
        /// <seealso cref="Identify(Context, TimeSpan)"/>
        Task<bool> IdentifyAsync(Context context);

        /// <summary>
        /// Tells the client that all pending analytics events (if any) should be delivered as soon
        /// as possible. 
        /// </summary>
        /// <remarks>
        /// <para>
        /// This flush is asynchronous, so this method will return before it is complete. To wait for
        /// the flush to complete, use <see cref="FlushAndWait(TimeSpan)"/> instead (or, if you are done
        /// with the SDK, <see cref="LdClient.Dispose()"/>).
        /// </para>
        /// <para>
        /// For more information, see: <a href="https://docs.launchdarkly.com/sdk/features/flush#net-client-side">
        /// Flushing Events</a>.
        /// </para>
        /// </remarks>
        /// <seealso cref="FlushAndWait(TimeSpan)"/>
        /// <seealso cref="FlushAndWaitAsync(TimeSpan)"/>
        void Flush();

        /// <summary>
        /// Tells the client to deliver any pending analytics events synchronously now.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Unlike <see cref="Flush"/>, this method waits for event delivery to finish. The timeout parameter, if
        /// greater than zero, specifies the maximum amount of time to wait. If the timeout elapses before
        /// delivery is finished, the method returns early and returns false; in this case, the SDK may still
        /// continue trying to deliver the events in the background.
        /// </para>
        /// <para>
        /// If the timeout parameter is zero or negative, the method waits as long as necessary to deliver the
        /// events. However, the SDK does not retry event delivery indefinitely; currently, any network error
        /// or server error will cause the SDK to wait one second and retry one time, after which the events
        /// will be discarded so that the SDK will not keep consuming more memory for events indefinitely.
        /// </para>
        /// <para>
        /// The method returns true if event delivery either succeeded, or definitively failed, before the
        /// timeout elapsed. It returns false if the timeout elapsed.
        /// </para>
        /// <para>
        /// This method is also implicitly called if you call <see cref="LdClient.Dispose()"/>. The difference is
        /// that FlushAndWait does not shut down the SDK client.
        /// </para>
        /// <para>
        /// For more information, see: <a href="https://docs.launchdarkly.com/sdk/features/flush#net-server-side">
        /// Flushing Events</a>.
        /// </para>
        /// </remarks>
        /// <param name="timeout">the maximum time to wait</param>
        /// <returns>true if completed, false if timed out</returns>
        /// <seealso cref="Flush"/>
        /// <seealso cref="FlushAndWaitAsync(TimeSpan)"/>
        bool FlushAndWait(TimeSpan timeout);

        /// <summary>
        /// Tells the client to deliver any pending analytics events now, returning a Task that can be awaited.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is equivalent to <see cref="FlushAndWait(TimeSpan)"/>, but with asynchronous semantics so it
        /// does not block the calling thread. The difference between this and <see cref="Flush"/> is that you
        /// can await the task to simulate blocking behavior.
        /// </para>
        /// <para>
        /// For more information, see: <a href="https://docs.launchdarkly.com/sdk/features/flush#net-server-side">
        /// Flushing Events</a>.
        /// </para>
        /// </remarks>
        /// <param name="timeout">the maximum time to wait</param>
        /// <returns>a Task that resolves to true if completed, false if timed out</returns>
        /// <seealso cref="Flush"/>
        /// <seealso cref="FlushAndWait(TimeSpan)"/>
        Task<bool> FlushAndWaitAsync(TimeSpan timeout);
    }
}
