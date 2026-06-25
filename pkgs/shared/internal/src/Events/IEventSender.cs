using System;
using System.Threading.Tasks;

namespace LaunchDarkly.Sdk.Internal.Events
{
    /// <summary>
    /// An abstraction of the mechanism for sending event JSON data.
    /// </summary>
    /// <remarks>
    /// The only implementation the SDKs use is <see cref="DefaultEventSender"/>, but the interface
    /// allows us to use a test fixture in tests.
    /// </remarks>
    public interface IEventSender : IDisposable
    {
        Task<EventSenderResult> SendEventDataAsync(EventDataKind kind, byte[] data, int eventCount);
    }

    public enum EventDataKind
    {
        AnalyticsEvents,
        DiagnosticEvent
    };

    public enum DeliveryStatus
    {
        Succeeded,
        Failed,
        FailedAndMustShutDown
    };

    public struct EventSenderResult
    {
        public DeliveryStatus Status { get; private set; }
        public DateTime? TimeFromServer { get; private set; }

        public EventSenderResult(DeliveryStatus status, DateTime? timeFromServer)
        {
            Status = status;
            TimeFromServer = timeFromServer;
        }
    }
}
