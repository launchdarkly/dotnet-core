using System;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Internal.DataSources;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.DataSystem
{
    internal class FlagChangedFacade : IFlagChanged
    {
        private readonly DataSourceUpdatesImpl _dataSourceUpdates;

        public FlagChangedFacade(DataSourceUpdatesImpl dataSourceUpdates)
        {
            _dataSourceUpdates = dataSourceUpdates;
        }

        public event EventHandler<FlagChangeEvent> FlagChanged
        {
            add => _dataSourceUpdates.FlagChanged += value;
            remove => _dataSourceUpdates.FlagChanged -= value;
        }
    }
}
