using System;
using System.Collections.Generic;
using LaunchDarkly.Sdk;

// Note, in order for System.Text.Json serialization/deserialization to work correctly, the members of
// this class must be properties with get/set, rather than fields. The property names are automatically
// camelCased by System.Text.Json.

namespace TestService
{
    public class Status
    {
        public string Name { get; set; }
        public string[] Capabilities { get; set; }
        public string ClientVersion { get; set; }
    }

    public class CreateInstanceParams
    {
        public SdkConfigParams Configuration { get; set; }
        public string Tag { get; set; }
    }

    public class SdkConfigParams
    {
        public string Credential { get; set; }
        public long? StartWaitTimeMs { get; set; }
        public bool InitCanFail { get; set; }
        public SdkConfigStreamParams Streaming { get; set; }
        public SdkConfigPollingParams Polling { get; set; }
        public SdkConfigEventParams Events { get; set; }
        public SdkConfigServiceEndpointsParams ServiceEndpoints { get; set; }
        public SdkClientSideParams ClientSide { get; set; }
        public SdkConfigTagsParams Tags { get; set; }
    }

    public class SdkConfigStreamParams
    {
        public Uri BaseUri { get; set; }
        public long? InitialRetryDelayMs { get; set; }
    }

    public class SdkConfigPollingParams
    {
        public Uri BaseUri { get; set; }
        public long? PollIntervalMs { get; set; }
    }

    public class SdkConfigEventParams
    {
        public Uri BaseUri { get; set; }
        public bool AllAttributesPrivate { get; set; }
        public int? Capacity { get; set; }
        public bool EnableDiagnostics { get; set; }
        public string[] GlobalPrivateAttributes { get; set; }
        public long? FlushIntervalMs { get; set; }
    }

    public class SdkConfigServiceEndpointsParams
    {
        public Uri Streaming { get; set; }
        public Uri Polling { get; set; }
        public Uri Events { get; set; }
    }

    public class SdkConfigTagsParams
    {
        public string ApplicationId { get; set; }
        public string ApplicationName { get; set; }
        public string ApplicationVersion { get; set; }
        public string ApplicationVersionName { get; set; }
    }

    public class SdkClientSideParams
    {
        public bool? EvaluationReasons { get; set; }
        public Context? InitialContext { get; set; }
        public User InitialUser { get; set; }
        public bool? UseReport { get; set; }
        public bool? IncludeEnvironmentAttributes { get; set; }
    }

    public class CommandParams
    {
        public string Command { get; set; }
        public EvaluateFlagParams Evaluate { get; set; }
        public EvaluateAllFlagsParams EvaluateAll { get; set; }
        public IdentifyEventParams IdentifyEvent { get; set; }
        public CustomEventParams CustomEvent { get; set; }
        public ContextBuildParams ContextBuild { get; set; }
        public ContextConvertParams ContextConvert { get; set; }
    }

    public class EvaluateFlagParams
    {
        public string FlagKey { get; set; }
        public String ValueType { get; set; }
        public LdValue Value { get; set; }
        public LdValue DefaultValue { get; set; }
        public bool Detail { get; set; }
    }

    public class EvaluateFlagResponse
    {
        public LdValue Value { get; set; }
        public int? VariationIndex { get; set; }
        public EvaluationReason? Reason { get; set; }
    }

    public class EvaluateAllFlagsParams
    {
    }

    public class EvaluateAllFlagsResponse
    {
        public IDictionary<string, LdValue> State { get; set; }
    }

    public class IdentifyEventParams
    {
        public Context? Context { get; set; }
        public User User { get; set; }
    }

    public class CustomEventParams
    {
        public string EventKey { get; set; }
        public LdValue Data { get; set; }
        public bool OmitNullData { get; set; }
        public double? MetricValue { get; set; }
    }

    public class ContextBuildParams
    {
        public ContextBuildSingleParams Single { get; set; }
        public ContextBuildSingleParams[] Multi { get; set; }
    }

    public class ContextBuildSingleParams
    {
        public string Kind { get; set; }
        public string Key { get; set; }
        public string Name { get; set; }
        public bool Anonymous { get; set; }
        public string[] Private { get; set; }
        public Dictionary<string, LdValue> Custom { get; set; }
    }

    public class ContextBuildResponse
    {
        public string Output { get; set; }
        public string Error { get; set; }
    }

    public class ContextConvertParams
    {
        public string Input { get; set; }
    }
}
