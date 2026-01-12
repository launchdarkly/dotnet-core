namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    /// <summary>
    /// Represents an action that can be enqueued and executed by the CompositeSource.
    /// </summary>
    internal interface ICompositeAction
    {
        /// <summary>
        /// Executes the action.
        /// </summary>
        void Execute();

        /// <summary>
        /// Gets a value indicating whether this is a final/terminal action.
        /// When true, this action should be the last action processed.
        /// </summary>
        bool IsFinal { get; }
    }
}