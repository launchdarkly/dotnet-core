using System;
using LaunchDarkly.Sdk.Internal.Concurrent;
using LaunchDarkly.Sdk.Internal.Events;

namespace LaunchDarkly.Sdk.Client.Internal.Events
{
    // The IDiagnosticDisabler interface is defined by the event processor implementation
    // in LaunchDarkly.InternalSdk as a hook to give the event processor a way to know
    // whether periodic diagnostic events should be temporarily disabled. The event
    // processor will register itself as an event handler on the DisabledChanged event.
    // In the client-side SDK, we disable periodic diagnostic events whenever the app is
    // in the background. (The server-side SDK doesn't have any equivalent behavior so
    // we do not implement IDiagnosticDisabler there.)

    internal sealed class DiagnosticDisablerImpl : IDiagnosticDisabler
    {
        private readonly AtomicBoolean _disabled = new AtomicBoolean(false);

        public bool Disabled => _disabled.Get();

        public event EventHandler<DisabledChangedArgs> DisabledChanged;

        internal void SetDisabled(bool disabled)
        {
            if (_disabled.GetAndSet(disabled) != disabled)
            {
                DisabledChanged?.Invoke(null, new DisabledChangedArgs(disabled));
                // We are not using TaskExecutor to dispatch this event because the
                // event handler, if any, was not provided by the application - it is
                // always our own internal logic in the LaunchDarkly.InternalSdk
                // events code, which doesn't do anything time-consuming. So we are
                // not calling out to unknown code and it's safe to be synchronous.
            }
        }
    }
}
