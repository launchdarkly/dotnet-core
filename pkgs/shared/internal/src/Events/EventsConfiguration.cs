using System;
using System.Collections.Immutable;

namespace LaunchDarkly.Sdk.Internal.Events
{
    /// <summary>
    /// Internal configuration properties for the events system.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The corresponding properties may or may not be configurable in the public SDK APIs.
    /// </para>
    /// <para>
    /// For simplicity in construction, this is a mutable class, but components should not
    /// modify its properties after passing it to another component. This is not a major
    /// risk since we do not expose this object in the public API and the SDKs have no
    /// reason to retain it after creating the event components.
    /// </para>
    /// <para>
    /// Only options that affect the common events implementation code in
    /// LaunchDarkly.InternalSdk are included here. Anything that is specific to
    /// LaunchDarkly.ServerSdk or LaunchDarkly.ClientSdk is not included: for instance,
    /// the cache settings for context deduplication are not included, because that
    /// behavior is implemented only in the server-side SDK.
    /// </para>
    /// </remarks>
    public sealed class EventsConfiguration
    {
        public bool AllAttributesPrivate { get; set; }

        public TimeSpan DiagnosticRecordingInterval { get; set; }

        public Uri DiagnosticUri { get; set; }

        public int EventCapacity { get; set; }

        public TimeSpan EventFlushInterval { get; set; }

        public Uri EventsUri { get; set; }

        public IImmutableSet<AttributeRef> PrivateAttributes { get; set;  }

        public TimeSpan? RetryInterval { get; set; }

        /// <summary>
        /// True if the events system should emit a separate summary event for each evaluation
        /// context, with the context attached, instead of a single aggregated summary.
        /// </summary>
        /// <remarks>
        /// Client-side SDKs enable this. Server-side SDKs leave it at the default of false, which
        /// preserves the original single aggregated summary behavior.
        /// </remarks>
        public bool PerContextSummaries { get; set; }
    }
}
