using System;

namespace LaunchDarkly.Sdk.Client.Interfaces
{
    /// <summary>
    /// An interface for tracking changes in feature flag configurations.
    /// </summary>
    /// <remarks>
    /// An implementation of this interface is returned by <see cref="ILdClient.FlagTracker"/>.
    /// Application code never needs to implement this interface.
    /// </remarks>
    public interface IFlagTracker
    {
        /// <summary>
        /// An event for receiving notifications of feature flag changes.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This event is raised whenever the SDK receives a variation for any feature flag that is
        /// not equal to its previous variation. This could mean that the flag configuration was
        /// changed in LaunchDarkly, or that you have changed the current user and the flag values
        /// are different for this user than for the previous user. The event is not raised if the
        /// SDK has just received its very first set of flag values.
        /// </para>
        /// <para>
        /// Currently this event will not fire in a scenario where 1. the client is offline, 2.
        /// <see cref="LdClient.Identify(Context, TimeSpan)"/> or <see cref="LdClient.IdentifyAsync(Context)"/>
        /// has been called to change the current user, and 3. the SDK had previously stored flag data
        /// for that user (see <see cref="Integrations.PersistenceConfigurationBuilder"/>) and has
        /// now loaded those flags. The event will only fire if the SDK has received new flag data
        /// from LaunchDarkly or from <see cref="Integrations.TestData"/>.
        /// </para>
        /// <para>
        /// Notifications will be dispatched either on the main thread (on mobile platforms) or in a
        /// background task (on all other platforms). It is the listener's responsibility to return
        /// as soon as possible so as not to block subsequent notifications.
        /// </para>
        /// </remarks>
        /// <example>
        ///     client.FlagTracker.FlagChanged += (sender, eventArgs) =>
        ///         {
        ///             System.Console.WriteLine("flag '" + eventArgs.Key
        ///                 + "' changed from " + eventArgs.OldValue
        ///                 + " to " + eventArgs.NewValue);
        ///         };
        /// </example>
        event EventHandler<FlagValueChangeEvent> FlagValueChanged;
    }

    /// <summary>
    /// A parameter class used with <see cref="IFlagTracker.FlagValueChanged"/>.
    /// </summary>
    /// <remarks>
    /// This is not an analytics event to be sent to LaunchDarkly; it is a notification to the
    /// application.
    /// </remarks>
    public struct FlagValueChangeEvent
    {
        /// <summary>
        /// The key of the feature flag whose configuration has changed.
        /// </summary>
        /// <remarks>
        /// The specified flag may have been modified directly, or this may be an indirect
        /// change due to a change in some other flag that is a prerequisite for this flag, or
        /// a user segment that is referenced in the flag's rules.
        /// </remarks>
        public string Key { get; }

        /// <summary>
        /// The last known value of the flag for the specified user prior to the update.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Since flag values can be of any JSON data type, this is represented as
        /// <see cref="LdValue"/>. That class has properties for converting to other .NET types,
        /// such as <see cref="LdValue.AsBool"/>.
        /// </para>
        /// <para>
        /// If the flag was deleted or could not be evaluated, this will be <see cref="LdValue.Null"/>.
        /// there is no application default value parameter as there is for the <c>Variation</c>
        /// methods; it is up to your code to substitute whatever fallback value is appropriate.
        /// </para>
        /// </remarks>
        public LdValue OldValue { get; }

        /// <summary>
        /// The new value of the flag for the specified user.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Since flag values can be of any JSON data type, this is represented as
        /// <see cref="LdValue"/>. That class has properties for converting to other .NET types,
        /// such as <see cref="LdValue.AsBool"/>.
        /// </para>
        /// <para>
        /// If the flag was deleted or could not be evaluated, this will be <see cref="LdValue.Null"/>.
        /// there is no application default value parameter as there is for the <c>Variation</c>
        /// methods; it is up to your code to substitute whatever fallback value is appropriate.
        /// </para>
        /// </remarks>
        public LdValue NewValue { get; }

        /// <summary>
        /// True if the flag was completely removed from the environment.
        /// </summary>
        public bool Deleted { get; }

        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        /// <param name="key">the key of the feature flag whose configuration has changed</param>
        /// <param name="oldValue">the last known value of the flag for the specified user prior to
        /// the update</param>
        /// <param name="newValue">the new value of the flag for the specified user</param>
        /// <param name="deleted">true if the flag was deleted</param>
        public FlagValueChangeEvent(string key, LdValue oldValue, LdValue newValue, bool deleted)
        {
            Key = key;
            OldValue = oldValue;
            NewValue = newValue;
            Deleted = deleted;
        }
    }
}
