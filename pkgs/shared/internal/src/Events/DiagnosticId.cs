using System;

namespace LaunchDarkly.Sdk.Internal.Events
{
    public sealed class DiagnosticId
    {
        public readonly Guid Id;
        public readonly string SdkKeySuffix;

        public DiagnosticId(string sdkKey, Guid diagnosticId)
        {
            if (sdkKey != null)
            {
                SdkKeySuffix = sdkKey.Substring(Math.Max(0, sdkKey.Length - 6));
            }
            Id = diagnosticId;
        }
    }
}
