using System;

namespace LaunchDarkly.Sdk.Internal.Events
{
    /// <summary>
    /// Interface for a strategy for removing duplicate contexts from the event stream. This has
    /// been factored out of <see cref="EventProcessor"/> because the client-side and
    /// server-side clients behave differently (client-side does not send index events).
    /// </summary>
    public interface IContextDeduplicator
    {
        /// <summary>
        /// The interval, if any, at which the event processor should call Flush.
        /// </summary>
        TimeSpan? FlushInterval { get; }

        /// <summary>
        /// Updates the internal state if necessary to reflect that we have seen the given context.
        /// Returns true if it is time to insert an index event for this context into the event output.
        /// </summary>
        /// <param name="context">a context object</param>
        /// <returns>true if an index event should be emitted</returns>
        bool ProcessContext(in Context context);

        /// <summary>
        /// Forgets any cached context information, so all subsequent contexs will be treated as new.
        /// </summary>
        void Flush();
    }
}
