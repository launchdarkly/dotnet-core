using System.Collections.Generic;

namespace LaunchDarkly.Sdk.Internal.Events
{
    /// <summary>
    /// Strategy for summarizing evaluation events into summary events.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementations provide either a single aggregated summary covering all contexts, or one
    /// summary per context with the context attached. The behavior is selected by the events
    /// configuration so that client-side and server-side SDKs can differ without changing the
    /// rest of the event pipeline.
    /// </para>
    /// <para>
    /// Implementations are deliberately not thread-safe; they are always invoked from the event
    /// processor's single message-processing thread.
    /// </para>
    /// </remarks>
    internal interface IEventSummarizer
    {
        // Adds information about an evaluation to the summary.
        void SummarizeEvent(
            UnixMillisecondTime timestamp,
            string flagKey,
            int? flagVersion,
            int? variation,
            in LdValue value,
            in LdValue defaultValue,
            in Context context
            );

        // Returns the current summary data and resets the state to empty. The aggregated
        // implementation returns at most one summary; the per-context implementation returns one
        // per context that had events. Empty summaries are not included.
        IReadOnlyList<EventSummary> GetSummariesAndReset();

        // Discards all accumulated summary data.
        void Clear();
    }
}
