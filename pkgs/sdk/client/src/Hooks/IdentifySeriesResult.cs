namespace LaunchDarkly.Sdk.Client.Hooks
{
    /// <summary>
    /// IdentifySeriesResult contains the outcome of an identify operation.
    /// </summary>
    public sealed class IdentifySeriesResult
    {
        /// <summary>
        /// Represents the possible statuses of an identify operation.
        /// </summary>
        public enum IdentifySeriesStatus
        {
            /// <summary>
            /// The identify operation completed successfully.
            /// </summary>
            Completed,

            /// <summary>
            /// The identify operation encountered an error.
            /// </summary>
            Error
        }

        /// <summary>
        /// The status of the identify operation.
        /// </summary>
        public IdentifySeriesStatus Status { get; }

        /// <summary>
        /// Constructs a new IdentifySeriesResult.
        /// </summary>
        /// <param name="status">the status of the identify operation</param>
        public IdentifySeriesResult(IdentifySeriesStatus status)
        {
            Status = status;
        }
    }
}
