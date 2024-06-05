using System;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Client.Subsystems;

namespace LaunchDarkly.Sdk.Client.Internal
{
    internal static class ComponentsImpl
    {
        internal sealed class NullDataSourceFactory : IComponentConfigurer<IDataSource>
        {
            public IDataSource Build(LdClientContext context) =>
                new NullDataSource();
        }

        internal sealed class NullDataSource : IDataSource
        {
            public bool Initialized => true;

            public void Dispose() { }

            public Task<bool> Start() => Task.FromResult(true);
        }

        internal sealed class NullEventProcessorFactory : IComponentConfigurer<IEventProcessor>
        {
            internal static readonly NullEventProcessorFactory Instance = new NullEventProcessorFactory();

            public IEventProcessor Build(LdClientContext context) =>
                NullEventProcessor.Instance;
        }

        internal sealed class NullEventProcessor : IEventProcessor
        {
            internal static readonly NullEventProcessor Instance = new NullEventProcessor();

            public void Dispose() { }

            public void Flush() { }

            public bool FlushAndWait(TimeSpan timeout) => true;

            public Task<bool> FlushAndWaitAsync(TimeSpan timeout) => Task.FromResult(true);

            public void RecordCustomEvent(in EventProcessorTypes.CustomEvent e) { }

            public void RecordEvaluationEvent(in EventProcessorTypes.EvaluationEvent e) { }

            public void RecordIdentifyEvent(in EventProcessorTypes.IdentifyEvent e) { }

            public void SetOffline(bool offline) { }
        }

    }
}
