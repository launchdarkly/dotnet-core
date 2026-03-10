using System;

namespace LaunchDarkly.Sdk.Client.Hooks
{
    /// <summary>
    /// IdentifySeriesContext represents parameters associated with an identify operation. It is
    /// made available in <see cref="Hook"/> stage callbacks.
    /// </summary>
    public sealed class IdentifySeriesContext
    {
        /// <summary>
        /// The Context being identified.
        /// </summary>
        public Context Context { get; }

        /// <summary>
        /// The timeout for the identify operation. A value of <see cref="TimeSpan.Zero"/> indicates
        /// that no timeout was specified.
        /// </summary>
        public TimeSpan Timeout { get; }

        /// <summary>
        /// Constructs a new IdentifySeriesContext.
        /// </summary>
        /// <param name="context">the context being identified</param>
        /// <param name="timeout">the timeout for the identify operation</param>
        public IdentifySeriesContext(Context context, TimeSpan timeout)
        {
            Context = context;
            Timeout = timeout;
        }
    }
}
