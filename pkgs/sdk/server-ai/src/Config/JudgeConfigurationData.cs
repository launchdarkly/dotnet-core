using System.Collections.Generic;

namespace LaunchDarkly.Sdk.Server.Ai.Config;

internal sealed class JudgeEntry
{
    public string Key { get; }
    public double SamplingRate { get; }

    internal JudgeEntry(string key, double samplingRate)
    {
        Key = key;
        SamplingRate = samplingRate;
    }
}

internal sealed class JudgeConfigurationData
{
    public IReadOnlyList<JudgeEntry> Judges { get; }

    internal JudgeConfigurationData(IReadOnlyList<JudgeEntry> judges)
    {
        Judges = judges;
    }
}
