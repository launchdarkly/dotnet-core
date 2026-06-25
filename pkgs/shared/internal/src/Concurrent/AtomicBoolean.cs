using System.Threading;

namespace LaunchDarkly.Sdk.Internal.Concurrent
{
    /// <summary>
    /// A simple atomic boolean using Interlocked.Exchange.
    /// </summary>
    public sealed class AtomicBoolean
    {
        private volatile int _value;

        /// <summary>
        /// Creates an instance.
        /// </summary>
        /// <param name="value">the initial value</param>
        public AtomicBoolean(bool value)
        {
            _value = value ? 1 : 0;
        }

        /// <summary>
        /// Returns the current value.
        /// </summary>
        /// <returns>the current value</returns>
        public bool Get() => _value != 0;

        /// <summary>
        /// Atomically updates the value and returns the previous value.
        /// </summary>
        /// <param name="newValue">the new value</param>
        /// <returns>the previous value</returns>
        public bool GetAndSet(bool newValue)
        {
            int old = Interlocked.Exchange(ref _value, newValue ? 1 : 0);
            return old != 0;
        }
    }
}
