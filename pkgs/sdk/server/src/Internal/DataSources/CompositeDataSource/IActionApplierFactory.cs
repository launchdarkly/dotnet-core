using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{

    internal interface IActionApplierFactory
    {
        /// <summary>
        /// Creates a new <see cref="IActionApplier"/> instance.
        /// </summary>
        /// <param name="actionable">the <see cref="ICompositeSourceActionable"/> is the entity that actions should be applied to</param>
        /// <returns>a new <see cref="IActionApplier"/> instance</returns>
        IActionApplier CreateActionApplier(ICompositeSourceActionable actionable);
    }
}
