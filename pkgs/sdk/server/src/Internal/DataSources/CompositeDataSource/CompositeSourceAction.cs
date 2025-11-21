namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    /// <summary>
    /// Represents an action that can be executed on a <see cref="CompositeSource"/>.
    /// </summary>
    internal abstract class CompositeSourceAction
    {
        /// <summary>
        /// Executes this action against the specified composite source.
        /// </summary>
        /// <param name="compositeSource">the composite source to act upon</param>
        public abstract void Accept(ICompositeSourceActionable compositeSource);
    }
}


