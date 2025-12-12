using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    /// <summary>
    /// A function type that creates a new <see cref="IDataSourceObserver"/> instance that is capable of applying actions to a composite data source.
    /// </summary>
    /// <param name="actionable">the <see cref="ICompositeSourceActionable"/> is the entity that actions should be applied to</param>
    /// <returns>a new <see cref="IDataSourceObserver"/> instance</returns>
    internal delegate IDataSourceObserver ActionApplierFactory(ICompositeSourceActionable actionable);
}

