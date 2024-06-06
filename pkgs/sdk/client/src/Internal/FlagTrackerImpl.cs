using System;
using LaunchDarkly.Sdk.Client.Interfaces;
using LaunchDarkly.Sdk.Client.Internal.DataSources;

namespace LaunchDarkly.Sdk.Client.Internal
{
    internal sealed class FlagTrackerImpl : IFlagTracker
    {
        private readonly DataSourceUpdateSinkImpl _updateSink;

        public event EventHandler<FlagValueChangeEvent> FlagValueChanged
        {
            add =>_updateSink.FlagValueChanged += value;
            remove => _updateSink.FlagValueChanged -= value;
        }

        internal FlagTrackerImpl(
            DataSourceUpdateSinkImpl updateSink
            )
        {
            _updateSink = updateSink;
        }
    }
}
