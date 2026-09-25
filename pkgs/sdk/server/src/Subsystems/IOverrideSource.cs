using System;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Subsystems
{
    /// <summary>
    /// Interface for a component that supplies flag and segment overrides. Overrides take precedence
    /// over data received from LaunchDarkly at evaluation time, on a per-key basis.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Overrides exist for resilience during an incident. They let an operator force one or more flags
    /// to a known state on a running client, whether or not the client can reach LaunchDarkly. Flags
    /// not present in the override data are unaffected.
    /// </para>
    /// <para>
    /// An override source is not a data source. It does not take part in the data system's initializer
    /// and synchronizer pipeline, and the override layer it populates has no effect on the client's
    /// initialization status, data availability, or data source status.
    /// </para>
    /// <para>
    /// The SDK constructs the source from the <see cref="IComponentConfigurer{T}"/> given to
    /// <see cref="Integrations.DataSystemBuilder.Overrides(IComponentConfigurer{IOverrideSource})"/>,
    /// starts it when the client starts, and disposes it when the client is disposed.
    /// </para>
    /// <para>
    /// Flag overrides are currently experimental and subject to change.
    /// </para>
    /// </remarks>
    public interface IOverrideSource : IDisposable
    {
        /// <summary>
        /// Starts the source.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The source performs its initial load before this method returns, so that overrides present
        /// when the client starts are in effect from the first evaluation. It then supplies a full
        /// replacement snapshot to the sink whenever its backing data changes, until it is disposed.
        /// A failed load leaves the previously supplied snapshot in place by not calling the sink.
        /// </para>
        /// <para>
        /// The SDK calls this method at most once, before any call to <see cref="IDisposable.Dispose"/>.
        /// </para>
        /// </remarks>
        /// <param name="sink">receives the override snapshots</param>
        void Start(IOverrideSink sink);
    }

    /// <summary>
    /// Receives the contents of the SDK's override layer from an <see cref="IOverrideSource"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The SDK implements this interface and passes it to <see cref="IOverrideSource.Start"/>.
    /// Override sources call it. They do not implement it.
    /// </para>
    /// <para>
    /// Flag overrides are currently experimental and subject to change.
    /// </para>
    /// </remarks>
    public interface IOverrideSink
    {
        /// <summary>
        /// Replaces the entire override layer with the given flags and segments.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Each call is a full snapshot. Entries absent from the call are removed from the layer, and
        /// an empty data set clears the layer.
        /// </para>
        /// <para>
        /// The data set uses the SDK's standard data kinds, <see cref="DataModel.Features"/> and
        /// <see cref="DataModel.Segments"/>, with items produced by their
        /// <see cref="DataKind.Deserialize(string)"/> methods. Sources supply ordinary, fully parsed
        /// definitions. The SDK itself marks the entries as overrides.
        /// </para>
        /// <para>
        /// This method is safe to call from any thread. Calls are serialized by the SDK, and the new
        /// layer contents are visible to evaluations when the call returns.
        /// </para>
        /// </remarks>
        /// <param name="data">the complete set of override flags and segments</param>
        void SetOverrides(FullDataSet<ItemDescriptor> data);
    }
}
