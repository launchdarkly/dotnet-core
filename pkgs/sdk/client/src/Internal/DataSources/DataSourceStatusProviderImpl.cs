using System;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Client.Interfaces;
using LaunchDarkly.Sdk.Client.Subsystems;
using LaunchDarkly.Sdk.Internal.Concurrent;

namespace LaunchDarkly.Sdk.Client.Internal.DataSources
{
    internal sealed class DataSourceStatusProviderImpl : IDataSourceStatusProvider
    {
        private readonly DataSourceUpdateSinkImpl _updateSink;

        public event EventHandler<DataSourceStatus> StatusChanged
        {
            add => _updateSink.StatusChanged += value;
            remove => _updateSink.StatusChanged -= value;
        }

        public DataSourceStatus Status => _updateSink.CurrentStatus;

        internal DataSourceStatusProviderImpl(DataSourceUpdateSinkImpl updateSink)
        {
            _updateSink = updateSink;
        }

        public bool WaitFor(DataSourceState desiredState, TimeSpan timeout) =>
            AsyncUtils.WaitSafely(() => _updateSink.WaitForAsync(desiredState, timeout));

        public Task<bool> WaitForAsync(DataSourceState desiredState, TimeSpan timeout) =>
            _updateSink.WaitForAsync(desiredState, timeout);
    }
}
