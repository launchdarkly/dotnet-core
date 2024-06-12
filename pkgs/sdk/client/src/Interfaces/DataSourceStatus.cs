using System;
using System.IO;
using System.Text;

namespace LaunchDarkly.Sdk.Client.Interfaces
{
    /// <summary>
    /// Information about the data source's status and about the last status change.
    /// </summary>
    /// <seealso cref="IDataSourceStatusProvider.Status"/>
    /// <seealso cref="IDataSourceStatusProvider.StatusChanged"/>
    public struct DataSourceStatus
    {
        /// <summary>
        /// An enumerated value representing the overall current state of the data source.
        /// </summary>
        public DataSourceState State { get; set; }

        /// <summary>
        /// The date/time that the value of <see cref="State"/> most recently changed.
        /// </summary>
        /// <remarks>
        /// The meaning of this depends on the current state:
        /// <list type="bullet">
        /// <item><description> For <see cref="DataSourceState.Initializing"/>, it is the time that the SDK started
        /// initializing. </description></item>
        /// <item><description> For <see cref="DataSourceState.Valid"/>, it is the time that the data source most
        /// recently entered a valid state, after previously having been <see cref="DataSourceState.Initializing"/>
        /// or an invalid state such as <see cref="DataSourceState.Interrupted"/>. </description></item>
        /// <item><description> For <see cref="DataSourceState.Interrupted"/>, it is the time that the data source
        /// most recently entered an error state, after previously having been <see cref="DataSourceState.Valid"/>.
        /// </description></item>
        /// <item><description> For <see cref="DataSourceState.BackgroundDisabled"/>,
        /// <see cref="DataSourceState.NetworkUnavailable"/>, or <see cref="DataSourceState.SetOffline"/>, it is
        /// the time that the SDK switched off the data source after detecting one of those conditions.
        /// </description></item>
        /// <item><description> For <see cref="DataSourceState.Shutdown"/>, it is the time that the data source
        /// encountered an unrecoverable error or that the SDK was explicitly shut down. </description></item>
        /// </list>
        /// </remarks>
        public DateTime StateSince { get; set; }

        /// <summary>
        /// Information about the last error that the data source encountered, if any.
        /// </summary>
        /// <remarks>
        /// This property should be updated whenever the data source encounters a problem, even if it does not cause
        /// <see cref="State"/> to change. For instance, if a stream connection fails and the state changes to
        /// <see cref="DataSourceState.Interrupted"/>, and then subsequent attempts to restart the connection also fail, the
        /// state will remain <see cref="DataSourceState.Interrupted"/> but the error information will be updated each time--
        /// and the last error will still be reported in this property even if the state later becomes
        /// <see cref="DataSourceState.Valid"/>.
        /// </remarks>
        public ErrorInfo? LastError { get; set; }

        /// <inheritdoc/>
        public override string ToString() =>
            string.Format("DataSourceStatus({0},{1},{2})", State, StateSince, LastError);

        /// <summary>
        /// A description of an error condition that the data source encountered.
        /// </summary>
        /// <seealso cref="DataSourceStatus.LastError"/>
        public struct ErrorInfo
        {
            /// <summary>
            /// An enumerated value representing the general category of the error.
            /// </summary>
            public ErrorKind Kind { get; set; }

            /// <summary>
            /// The HTTP status code if the error was <see cref="ErrorKind.ErrorResponse"/>, or zero otherwise.
            /// </summary>
            public int StatusCode { get; set; }

            /// <summary>
            /// Any additional human-readable information relevant to the error.
            /// </summary>
            /// <remarks>
            /// The format of this message is subject to change and should not be relied on programmatically.
            /// </remarks>
            public string Message { get; set; }

            /// <summary>
            /// The date/time that the error occurred.
            /// </summary>
            public DateTime Time { get; set; }

            /// <summary>
            /// Constructs an instance based on an exception.
            /// </summary>
            /// <param name="e">the exception</param>
            /// <returns>an ErrorInfo</returns>
            public static ErrorInfo FromException(Exception e) => new ErrorInfo
            {
                Kind = e is IOException ? ErrorKind.NetworkError : ErrorKind.Unknown,
                Message = e.Message,
                Time = DateTime.Now
            };

            /// <summary>
            /// Constructs an instance based on an HTTP error status.
            /// </summary>
            /// <param name="statusCode">the status code</param>
            /// <returns>an ErrorInfo</returns>
            public static ErrorInfo FromHttpError(int statusCode) => new ErrorInfo
            {
                Kind = ErrorKind.ErrorResponse,
                StatusCode = statusCode,
                Time = DateTime.Now
            };

            /// <inheritdoc/>
            public override string ToString()
            {
                var s = new StringBuilder();
                s.Append(Kind.Identifier());
                if (StatusCode > 0 || !string.IsNullOrEmpty(Message))
                {
                    s.Append("(");
                    if (StatusCode > 0)
                    {
                        s.Append(StatusCode);
                    }
                    if (!string.IsNullOrEmpty(Message))
                    {
                        if (StatusCode > 0)
                        {
                            s.Append(",");
                        }
                        s.Append(Message);
                    }
                    s.Append(")");
                }
                s.Append("@");
                s.Append(Time);
                return s.ToString();
            }
        }

        /// <summary>
        /// An enumeration describing the general type of an error reported in <see cref="ErrorInfo"/>.
        /// </summary>
        public enum ErrorKind
        {
            /// <summary>
            /// An unexpected error, such as an uncaught exception, further described by <see cref="ErrorInfo.Message"/>.
            /// </summary>
            Unknown,

            /// <summary>
            /// An I/O error such as a dropped connection.
            /// </summary>
            NetworkError,

            /// <summary>
            /// The LaunchDarkly service returned an HTTP response with an error status, available with
            /// <see cref="ErrorInfo.StatusCode"/>.
            /// </summary>
            ErrorResponse,

            /// <summary>
            /// The SDK received malformed data from the LaunchDarkly service.
            /// </summary>
            InvalidData,

            /// <summary>
            /// The data source itself is working, but when it tried to put an update into the data store, the data
            /// store failed (so the SDK may not have the latest data).
            /// </summary>
            /// <remarks>
            /// Data source implementations do not need to report this kind of error; it will be automatically
            /// reported by the SDK whenever one of the update methods of <see cref="Subsystems.IDataSourceUpdateSink"/> throws an
            /// exception.
            /// </remarks>
            StoreError
        }
    }

    /// <summary>
    /// An enumeration of possible values for <see cref="DataSourceStatus.State"/>.
    /// </summary>
    public enum DataSourceState
    {
        /// <summary>
        /// The initial state of the data source when the SDK is being initialized.
        /// </summary>
        /// <remarks>
        /// If it encounters an error that requires it to retry initialization, the state will remain at
        /// <see cref="Initializing"/> until it either succeeds and becomes <see cref="Valid"/>, or
        /// permanently fails and becomes <see cref="Shutdown"/>.
        /// </remarks>
        Initializing,

        /// <summary>
        /// Indicates that the data source is currently operational and has not had any problems since the
        /// last time it received data.
        /// </summary>
        /// <remarks>
        /// In streaming mode, this means that there is currently an open stream connection and that at least
        /// one initial message has been received on the stream. In polling mode, it means that the last poll
        /// request succeeded.
        /// </remarks>
        Valid,

        /// <summary>
        /// Indicates that the data source encountered an error that it will attempt to recover from.
        /// </summary>
        /// <remarks>
        /// <para>
        /// In streaming mode, this means that the stream connection failed, or had to be dropped due to some
        /// other error, and will be retried after a backoff delay. In polling mode, it means that the last poll
        /// request failed, and a new poll request will be made after the configured polling interval.
        /// </para>
        /// <para>
        /// This is different from <see cref="NetworkUnavailable"/>, which would mean that the SDK knows the
        /// device is not online at all and is waiting for it to be online again.
        /// </para>
        /// </remarks>
        Interrupted,

        /// <summary>
        /// Indicates that the SDK is in background mode and background updating has been disabled.
        /// </summary>
        /// <remarks>
        /// On mobile devices, if the application containing the SDK is put into the background, by default
        /// the SDK will still check for feature flag updates occasionally. However, if this has been disabled
        /// with <c>EnableBackgroundUpdating(false)</c>, the SDK will instead stop the data source and wait
        /// until it is in the foreground again. During that time, the state is <c>BackgroundDisabled</c>.
        /// </remarks>
        /// <seealso cref="ConfigurationBuilder.EnableBackgroundUpdating(bool)"/>
        BackgroundDisabled,

        /// <summary>
        /// Indicates that the SDK is aware of a lack of network connectivity.
        /// </summary>
        /// <remarks>
        /// <para>
        /// On mobile devices, if wi-fi is turned off or there is no wi-fi connection and cellular data is
        /// unavailable, the device OS will tell the SDK that the network is unavailable. The SDK then enters
        /// this state, where it will not try to make any network connections since they would be guaranteed to
        /// fail, until the OS informs it that the network is available again.
        /// </para>
        /// <para>
        /// This is different from <see cref="Interrupted"/>, which would mean that the SDK thinks network
        /// requests ought to be working but for some reason they are not (due to either a network problem
        /// that the device OS does not know about, or a problem with the service endpoint).
        /// </para>
        /// <para>
        /// The .NET Standard version of the SDK is not able to detect network status, so desktop applications
        /// will see the <see cref="Interrupted"/> state if the network is turned off.
        /// </para>
        /// </remarks>
        NetworkUnavailable,

        /// <summary>
        /// Indicates that the application has told the SDK to stay offline.
        /// </summary>
        /// <remarks>
        /// This means that either the SDK was originally configured with <c>Offline(true)</c> and has not been
        /// changed to be online since then, or that it was originally online but has been put offline with
        /// <see cref="LdClient.SetOffline(bool, TimeSpan)"/> or <see cref="LdClient.SetOfflineAsync(bool)"/>.
        /// It is not a permanent condition; the application can change this at any time.
        /// </remarks>
        /// <seealso cref="ConfigurationBuilder.Offline(bool)"/>
        /// <seealso cref="LdClient.SetOffline(bool, TimeSpan)"/>
        /// <seealso cref="LdClient.SetOfflineAsync(bool)"/>
        SetOffline,

        /// <summary>
        /// Indicates that the data source has been permanently shut down.
        /// </summary>
        /// <remarks>
        /// This could be because it encountered an unrecoverable error (for instance, the LaunchDarkly service
        /// rejected the SDK key; an invalid SDK key will never become valid), or because the SDK client was
        /// explicitly shut down.
        /// </remarks>
        Shutdown
    }

    /// <summary>
    /// Extension helper methods for use with data source status types.
    /// </summary>
    public static class DataSourceStatusExtensions
    {
        /// <summary>
        /// Returns a standardized string identifier for a <see cref="DataSourceState"/>.
        /// </summary>
        /// <remarks>
        /// These Java-style uppercase identifiers (<c>INITIALIZING</c>, <c>VALID</c>, etc.) may be used in
        /// logging for consistency across SDKs.
        /// </remarks>
        /// <param name="state">a state value</param>
        /// <returns>a string identifier</returns>
        public static string Identifier(this DataSourceState state)
        {
            switch (state)
            {
                case DataSourceState.Initializing:
                    return "INITIALIZING";
                case DataSourceState.Valid:
                    return "VALID";
                case DataSourceState.Interrupted:
                    return "INTERRUPTED";
                case DataSourceState.NetworkUnavailable:
                    return "NETWORK_UNAVAILABLE";
                case DataSourceState.SetOffline:
                    return "SET_OFFLINE";
                case DataSourceState.Shutdown:
                    return "SHUTDOWN";
                default:
                    return state.ToString();
            }
        }

        /// <summary>
        /// Returns a standardized string identifier for a <see cref="DataSourceStatus.ErrorKind"/>.
        /// </summary>
        /// <remarks>
        /// These Java-style uppercase identifiers (<c>ERROR_RESPONSE</c>, <c>NETWORK_ERROR</c>, etc.) may be
        /// used in logging for consistency across SDKs.
        /// </remarks>
        /// <param name="errorKind">an error kind value</param>
        /// <returns>a string identifier</returns>
        public static string Identifier(this DataSourceStatus.ErrorKind errorKind)
        {
            switch (errorKind)
            {
                case DataSourceStatus.ErrorKind.ErrorResponse:
                    return "ERROR_RESPONSE";
                case DataSourceStatus.ErrorKind.InvalidData:
                    return "INVALID_DATA";
                case DataSourceStatus.ErrorKind.NetworkError:
                    return "NETWORK_ERROR";
                case DataSourceStatus.ErrorKind.StoreError:
                    return "STORE_ERROR";
                case DataSourceStatus.ErrorKind.Unknown:
                    return "UNKNOWN";
                default:
                    return errorKind.ToString();
            }
        }
    }
}
