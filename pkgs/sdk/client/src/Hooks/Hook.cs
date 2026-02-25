using System;

namespace LaunchDarkly.Sdk.Client.Hooks
{
    /// <summary>
    /// A Hook is a set of user-defined callbacks that are executed by the SDK at various points
    /// of interest. To create your own hook with customized logic, derive from Hook and override its methods.
    /// </summary>
    public class Hook : IDisposable
    {
        /// <summary>
        /// Access this hook's <see cref="HookMetadata"/>.
        /// </summary>
        public HookMetadata Metadata { get; private set; }

        /// <summary>
        /// Constructs a new Hook with the given name. The name may be used in log messages.
        /// </summary>
        /// <param name="name">the name of the hook</param>
        public Hook(string name)
        {
            Metadata = new HookMetadata(name);
        }

        /// <summary>
        /// Disposes the hook. This method will be called when the SDK is disposed.
        /// </summary>
        /// <param name="disposing">true if the caller is Dispose, false if the caller is a finalizer</param>
        protected virtual void Dispose(bool disposing)
        {
        }

        /// <summary>
        /// Disposes the hook. This method will be called when the SDK is disposed.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// HookMetadata contains information related to a Hook which can be inspected by the SDK, or within
    /// a hook stage.
    /// </summary>
    public sealed class HookMetadata
    {
        /// <summary>
        /// Constructs a new HookMetadata with the given hook name.
        /// </summary>
        /// <param name="name">name of the hook. May be used in logs by the SDK</param>
        public HookMetadata(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Returns the name of the hook.
        /// </summary>
        public string Name { get; }
    }
}
