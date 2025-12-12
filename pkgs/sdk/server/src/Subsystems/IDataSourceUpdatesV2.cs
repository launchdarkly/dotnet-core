namespace LaunchDarkly.Sdk.Server.Subsystems
{
    /// <summary>
    /// Interfaces required by data source updates implementations in FDv2.
    /// </summary>
    internal interface IDataSourceUpdatesV2: IDataSourceUpdates, ITransactionalDataSourceUpdates, IDataSourceUpdatesHeaders
    {
        
    }
}
