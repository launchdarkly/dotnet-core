using System;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Client.Interfaces;

namespace LaunchDarkly.Sdk.Client.Subsystems
{
    /// <summary>
    /// Interface for an object that can send or store analytics events.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Application code normally does not need to interact with <see cref="IEventProcessor"/> or its
    /// related parameter types. They are provided to allow a custom implementation or test fixture to be
    /// substituted for the SDK's normal analytics event logic.
    /// </para>
    /// <para>
    /// All of the <c>Record</c> methods must return as soon as possible without waiting for events to be
    /// delivered; event delivery is done asynchronously by a background task.
    /// </para>
    /// </remarks>
    public interface IEventProcessor : IDisposable
    {
        /// <summary>
        /// Records the action of evaluating a feature flag.
        /// </summary>
        /// <remarks>
        /// Depending on the feature flag properties and event properties, this may be transmitted to the
        /// events service as an individual event, or may only be added into summary data.
        /// </remarks>
        /// <param name="e">parameters for an evaluation event</param>
        void RecordEvaluationEvent(in EventProcessorTypes.EvaluationEvent e);

        /// <summary>
        /// Records a set of user properties.
        /// </summary>
        /// <param name="e">parameters for an identify event</param>
        void RecordIdentifyEvent(in EventProcessorTypes.IdentifyEvent e);

        /// <summary>
        /// Records a custom event.
        /// </summary>
        /// <param name="e">parameters for a custom event</param>
        void RecordCustomEvent(in EventProcessorTypes.CustomEvent e);

        /// <summary>
        /// Puts the component into offline mode if appropriate.
        /// </summary>
        /// <param name="offline">true if the SDK has been put offline</param>
        void SetOffline(bool offline);

        /// <summary>
        /// Specifies that any buffered events should be sent as soon as possible.
        /// </summary>
        /// <seealso cref="ILdClient.Flush"/>
        void Flush();

        /// <summary>
        /// Delivers any pending analytics events synchronously now.
        /// </summary>
        /// <param name="timeout">the maximum time to wait</param>
        /// <returns>true if completed, false if timed out</returns>
        /// <seealso cref="ILdClient.FlushAndWait(TimeSpan)"/>
        bool FlushAndWait(TimeSpan timeout);

        /// <summary>
        /// Delivers any pending analytics events now, returning a Task that can be awaited.
        /// </summary>
        /// <param name="timeout">the maximum time to wait</param>
        /// <returns>a Task that resolves to true if completed, false if timed out</returns>
        /// <seealso cref="ILdClient.FlushAndWaitAsync(TimeSpan)"/>
        Task<bool> FlushAndWaitAsync(TimeSpan timeout);
    }
}
