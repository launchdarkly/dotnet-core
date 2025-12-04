using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    /// <summary>
    /// A function type that creates a new <see cref="IActionApplier"/> instance.
    /// </summary>
    /// <param name="actionable">the <see cref="ICompositeSourceActionable"/> is the entity that actions should be applied to</param>
    /// <returns>a new <see cref="IActionApplier"/> instance</returns>
    internal delegate IActionApplier ActionApplierFactory(ICompositeSourceActionable actionable);
}

