using System.Collections.Generic;

namespace LaunchDarkly.Sdk.Server.Ai.Config;

internal sealed class Judge
{
    public string Key { get; }
    public double SamplingRate { get; }

    internal Judge(string key, double samplingRate)
    {
        Key = key;
        SamplingRate = samplingRate;
    }
}

internal sealed class JudgeConfiguration
{
    public IReadOnlyList<Judge> Judges { get; }

    internal JudgeConfiguration(IReadOnlyList<Judge> judges)
    {
        Judges = judges;
    }
}
