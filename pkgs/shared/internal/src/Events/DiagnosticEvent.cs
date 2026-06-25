
namespace LaunchDarkly.Sdk.Internal.Events
{
    /// <summary>
    /// Represents a unit of JSON data that will be sent as a diagnostic event. This is simply a wrapper for
    /// an immutable and non-nullable <see cref="LdValue"/>, but using the DiagnosticEvent type makes it
    /// clearer what the purpose of the data is.
    /// </summary>
    public struct DiagnosticEvent
    {
        private readonly LdValue _jsonValue;

        public LdValue JsonValue => _jsonValue;

        public DiagnosticEvent(LdValue jsonValue)
        {
            _jsonValue = jsonValue;
        }
    }
}
