using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Logging;

namespace LaunchDarkly.Sdk.Internal.Concurrent
{
    /// <summary>
    /// A generic mechanism for maintaining some kind of synchronized state that multiple
    /// tasks or threads can wait on.
    /// </summary>
    /// <typeparam name="StateT">a value type representing the current state</typeparam>
    /// <typeparam name="UpdateT">either the same as <c>StateT</c>, or any other type that
    /// you want to use as the parameter for <see cref="Update(UpdateT)"/></typeparam>
    public sealed class StateMonitor<StateT, UpdateT> : IDisposable where StateT : struct
    {
        internal sealed class Awaiter
        {
            internal Func<StateT, bool> TestFn { get; set; }
            internal TaskCompletionSource<StateT> Completion { get; set; }
        }

        private readonly Func<StateT, UpdateT, StateT?> _updateFn;
        private readonly object _stateLock = new object();
        private readonly Logger _log;

        private StateT _current;
        private LinkedList<Awaiter> _awaiters = new LinkedList<Awaiter>();

        /// <summary>
        /// The current state value.
        /// </summary>
        public StateT Current
        {
            get
            {
                lock (_stateLock)
                {
                    return _current;
                }
            }
        }

        /// <summary>
        /// Constructs an instance.
        /// </summary>
        /// <remarks>
        /// The <c>updateFn</c> parameter provides a flexible mechanism for atomically updating
        /// the state. It will be called under a lock each time <see cref="Update(UpdateT)"/>
        /// is called, receiving the current state and the update value as parameters; it returns
        /// either a new state, or <see langword="null"/> to skip updating the state.
        /// </remarks>
        /// <param name="initial">the initial state value</param>
        /// <param name="updateFn">the state transform function</param>
        /// <param name="log">optional logger for debug logging of state transitions</param>
        public StateMonitor(StateT initial, Func<StateT, UpdateT, StateT?> updateFn, Logger log)
        {
            _current = initial;
            _updateFn = updateFn;
            _log = log;
        }

        /// <summary>
        /// Atomically updates the state and notifies any tasks that were waiting on it, if
        /// appropriate.
        /// </summary>
        /// <remarks>
        /// This first passes the <paramref name="update"/> value to the state transform
        /// function that was configured in the constructor. If that function returns
        /// <see langword="null"/> (meaning the state should not be updated), nothing happens
        /// and this method returns <see langword="false"/> (while also returning the
        /// current state in <paramref name="newState"/>). Otherwise, it atomically
        /// updates the state, wakes up any tasks that had previously called
        /// <see cref="WaitFor(Func{StateT, bool}, TimeSpan)"/> or
        /// <see cref="WaitForAsync(Func{StateT, bool}, TimeSpan)"/> if the new state
        /// matches their conditions, and returns <see langword="true"/> along with the
        /// updated state.
        /// </remarks>
        /// <param name="update">a value representing a possible state change</param>
        /// <param name="newState">receives the result state</param>
        /// <returns><see langword="true"/> if the state was changed</returns>
        public bool Update(UpdateT update, out StateT newState)
        {
            List<TaskCompletionSource<StateT>> completed = null;
            lock (_stateLock)
            {
                var maybeNewState = _updateFn(_current, update);
                if (!maybeNewState.HasValue)
                {
                    newState = _current;
                    return false;
                }
                newState = maybeNewState.Value;
                _current = newState;
                if (_awaiters is null)
                {
                    return true; // we've already shut down
                }
                for (var node = _awaiters.First; node != null;)
                {
                    var next = node.Next;
                    if (node.Value.TestFn(newState))
                    {
                        if (completed is null)
                        {
                            completed = new List<TaskCompletionSource<StateT>>();
                        }
                        completed.Add(node.Value.Completion);
                        _awaiters.Remove(node);
                    }
                    node = next;
                }
            }
            _log?.Debug("Updated state to {0}", newState);
            if (completed != null)
            {
                foreach (var c in completed)
                {
                    c.TrySetResult(newState);
                }
            }
            return true;
        }

        internal void Forget(Awaiter awaiter)
        {
            lock (_stateLock)
            {
                _awaiters?.Remove(awaiter);
            }
        }

        /// <summary>
        /// Equivalent to <see cref="WaitForAsync(Func{WaitState}, TimeSpan)"/>, but uses
        /// <see cref="AsyncUtils.WaitSafely(Func{Task})"/> to convert it to a synchronous call.
        /// </summary>
        /// <param name="testFn">the test function</param>
        /// <param name="timeout">the timeout</param>
        /// <returns>the end state, or null if timed out</returns>
        public StateT? WaitFor(Func<StateT, bool> testFn, TimeSpan timeout) =>
            AsyncUtils.WaitSafely(() => WaitForAsync(testFn, timeout));

        /// <summary>
        /// Waits for the state to be updated to some value, defined by a test function.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method calls the test function while holding a lock on the lock object specified
        /// in the constructor. If the return value is <see langword="true"/>, it immediately
        /// returns whatever the current state value is; otherwise, it sleeps until the next time
        /// <see cref="Update(UpdateT, out StateT)"/> is called and then repeats the check-- unless the
        /// timeout expires, in which case it returns <see langword="null"/>.
        /// </para>
        /// </remarks>
        /// <param name="testFn">the test function</param>
        /// <param name="timeout">the timeout</param>
        /// <returns>the end state, or null if timed out</returns>
        public async Task<StateT?> WaitForAsync(Func<StateT, bool> testFn, TimeSpan timeout)
        {
            var completion = new TaskCompletionSource<StateT>();
            Awaiter awaiter;

            lock (_stateLock)
            {
                if (testFn(_current))
                {
                    return _current;
                }
                if (_awaiters is null) // this means we've been shut down
                {
                    return null;
                }
                awaiter = new Awaiter { TestFn = testFn, Completion = completion };
                _awaiters.AddFirst(awaiter);
            }

            var timeoutSignal = timeout > TimeSpan.Zero ? new CancellationTokenSource(timeout) :
                new CancellationTokenSource();
            using (timeoutSignal.Token.Register(() => completion.TrySetCanceled()))
            {
                try
                {
                    return await completion.Task;
                }
                catch (TaskCanceledException)
                {
                    Forget(awaiter);
                    return null;
                }
            }
        }

        public void Dispose()
        {
            LinkedList<Awaiter> oldAwaiters;
            lock (_stateLock)
            {
                oldAwaiters = _awaiters;
                _awaiters = null;
            }
            if (oldAwaiters != null)
            {
                foreach (var a in oldAwaiters)
                {
                    a.Completion.TrySetCanceled();
                }
            }
        }
    }
}
