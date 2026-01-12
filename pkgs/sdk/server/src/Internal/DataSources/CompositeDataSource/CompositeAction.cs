using System;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    /// <summary>
    /// Default implementation of ICompositeAction that wraps an Action delegate.
    /// </summary>
    internal class CompositeAction : ICompositeAction
    {
        private readonly Action _action;

        /// <summary>
        /// Creates a new CompositeAction that wraps an Action delegate.
        /// </summary>
        /// <param name="action">The action to execute</param>
        /// <param name="isFinal">Whether this is a final/terminal action</param>
        public CompositeAction(Action action, bool isFinal = false)
        {
            _action = action ?? throw new ArgumentNullException(nameof(action));
            IsFinal = isFinal;
        }

        /// <inheritdoc/>
        public void Execute()
        {
            _action();
        }

        /// <inheritdoc/>
        public bool IsFinal { get; }
    }
}