using System;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Internal.DataSources;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2DataSources
{
    internal static partial class FDv2DataSource
    {
        /// <summary>
        /// Combines multiple data source observers into a single observer.
        /// </summary>
        private class CompositeObserver : IDataSourceObserver
        {
            private readonly IDataSourceObserver[] _observers;

            public CompositeObserver(params IDataSourceObserver[] observers)
            {
                _observers = observers ?? throw new ArgumentNullException(nameof(observers));
            }

            public void Apply(DataStoreTypes.ChangeSet<DataStoreTypes.ItemDescriptor> changeSet, bool exhausted)
            {
                foreach (var dataSourceObserver in _observers)
                {
                    dataSourceObserver.Apply(changeSet, exhausted);
                }
            }

            public void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError)
            {
                foreach (var dataSourceObserver in _observers)
                {
                    dataSourceObserver.UpdateStatus(newState, newError);
                }
            }
        }
    }
}
