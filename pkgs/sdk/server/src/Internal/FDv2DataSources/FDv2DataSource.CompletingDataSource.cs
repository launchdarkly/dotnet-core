using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2DataSources
{
    internal static partial class FDv2DataSource
    {
        /// <summary>
        /// This class wraps an underlying composite data source abstracting the handling of initialization completion.
        /// </summary>
        private class CompletingDataSource : IDataSource
        {
            private readonly IDataSource _inner;
            private readonly InitializationTracker _tracker;

            public CompletingDataSource(IDataSource inner, InitializationTracker tracker)
            {
                _inner = inner;
                _tracker = tracker;
            }

            public void Dispose()
            {
                _inner.Dispose();
            }

            public Task<bool> Start()
            {
                _ = _inner.Start();
                return _tracker.Task;
            }

            public bool Initialized => _tracker.Task.IsCompleted && _tracker.Task.Result;
        }
    }
}
