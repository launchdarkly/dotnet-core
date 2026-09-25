using System;
using System.Collections.Generic;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Internal.DataSystem;
using LaunchDarkly.Sdk.Server.Subsystems;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.Overrides
{
    /// <summary>
    /// Applies override layer replacements supplied by an override source, and notifies flag change
    /// listeners of the flags affected by each replacement.
    /// </summary>
    internal sealed class OverrideSink : IOverrideSink
    {
        private readonly object _lock = new object();
        private readonly OverrideLayer _layer;
        private readonly IReadOnlyStore _base;
        private readonly Action<IEnumerable<string>> _notify;
        private readonly Func<bool> _hasListeners;
        private readonly Logger _log;

        /// <summary>
        /// Creates a sink that writes to the given layer.
        /// </summary>
        /// <param name="layer">the override layer</param>
        /// <param name="baseStore">the store holding LaunchDarkly data, without the overlay. Merged-view
        /// snapshots for change computation are built from it plus the layer.</param>
        /// <param name="notify">sends flag change notifications for the given flag keys</param>
        /// <param name="hasListeners">reports whether anything listens for flag changes</param>
        /// <param name="log">the destination for log output</param>
        internal OverrideSink(
            OverrideLayer layer,
            IReadOnlyStore baseStore,
            Action<IEnumerable<string>> notify,
            Func<bool> hasListeners,
            Logger log
            )
        {
            _layer = layer;
            _base = baseStore;
            _notify = notify;
            _hasListeners = hasListeners;
            _log = log;
        }

        /// <summary>
        /// Atomically replaces the entire override layer, then notifies listeners of every flag whose
        /// merged-view evaluation may have changed. Calls are serialized, so overlapping updates from
        /// a source cannot interleave.
        /// </summary>
        public void SetOverrides(FullDataSet<ItemDescriptor> data)
        {
            lock (_lock)
            {
                // Computing affected flags requires snapshots of the merged view before and after the
                // replacement. Skip all of that work when nothing is listening.
                if (!_hasListeners())
                {
                    _layer.SetAll(data, out _, out _);
                    return;
                }

                _layer.SetAll(data, out var previous, out var current);
                var oldMerged = OverrideChanges.SnapshotMergedView(_base, previous);
                var newMerged = OverrideChanges.SnapshotMergedView(_base, current);

                var affected = OverrideChanges.ComputeAffectedFlags(previous, current, oldMerged, newMerged);
                if (affected.Count > 0)
                {
                    _log.Debug("Override update affected {0} flag(s)", affected.Count);
                    _notify(affected);
                }
            }
        }
    }
}
