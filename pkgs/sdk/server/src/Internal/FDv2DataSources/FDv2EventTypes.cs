namespace LaunchDarkly.Sdk.Server.Internal.FDv2DataSources
{
    /// <summary>
    /// Types of events that FDv2 can receive.
    /// </summary>
    internal static class FDv2EventTypes
    {
        public const string ServerIntent = "server-intent";
        public const string PutObject = "put-object";
        public const string DeleteObject = "delete-object";
        public const string Error = "error";
        public const string Goodbye = "goodbye";
        public const string HeartBeat = "heartbeat";
        public const string PayloadTransferred = "payload-transferred";
    }
}
