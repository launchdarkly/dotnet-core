using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.DataSystem
{
    internal class SelectorSourceFacade : ISelectorSource
    {
        private readonly ITransactionalDataStore _store;

        public SelectorSourceFacade(ITransactionalDataStore store)
        {
            _store = store;
        }

        public Selector Selector => _store.Selector;
    }
}
