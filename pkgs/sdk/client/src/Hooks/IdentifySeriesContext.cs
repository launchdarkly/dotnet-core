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
        /// The timeout in seconds for the identify operation.
        /// </summary>
        public int Timeout { get; }

        /// <summary>
        /// Constructs a new IdentifySeriesContext.
        /// </summary>
        /// <param name="context">the context being identified</param>
        /// <param name="timeout">the timeout in seconds</param>
        public IdentifySeriesContext(Context context, int timeout)
        {
            Context = context;
            Timeout = timeout;
        }
    }
}
