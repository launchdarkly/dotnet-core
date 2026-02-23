namespace LaunchDarkly.Sdk.Server.Ai;

/// <summary>
/// Contains metadata about the AI SDK, such as its name, version, and implementation language.
/// </summary>
public static class SdkInfo
{
    /// <summary>
    /// The name of the AI SDK package.
    /// </summary>
    public const string Name = "LaunchDarkly.ServerSdk.Ai";

    /// <summary>
    /// The version of the AI SDK package.
    /// </summary>
    public const string Version = "0.9.1"; // x-release-please-version

    /// <summary>
    /// The implementation language.
    /// </summary>
    public const string Language = "dotnet";
}
