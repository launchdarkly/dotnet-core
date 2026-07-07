using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;
using LaunchDarkly.Sdk.Server.Ai.Tracking;

namespace LaunchDarkly.Sdk.Server.Ai.Evals;

/// <summary>
/// Runs one or more <see cref="Judge"/> instances against a single input/output pair and
/// collects their results.
/// </summary>
/// <remarks>
/// <para>
/// The evaluator does NOT call <c>TrackJudgeResult</c> — that is the responsibility of the
/// managed type (Plan D). Use <see cref="Noop"/> when evaluation is not required.
/// </para>
/// </remarks>
public sealed class Evaluator
{
    private static readonly Evaluator _noop = new Evaluator();

    private readonly IReadOnlyDictionary<string, Judge> _judges;
    private readonly LdAiConfigTypes.JudgeConfiguration _judgeConfiguration;
    private readonly ILogger _logger;
    private readonly bool _isNoop;
    private readonly Random _random;

    private Evaluator()
    {
        _isNoop = true;
        _judges = new Dictionary<string, Judge>();
        _judgeConfiguration = null;
        _logger = null;
    }

    /// <summary>
    /// Constructs an <see cref="Evaluator"/>.
    /// </summary>
    /// <param name="judges">a dictionary mapping judge keys to their <see cref="Judge"/> instances</param>
    /// <param name="judgeConfiguration">the judge configuration describing which judges to run and at
    /// what sampling rate</param>
    /// <param name="logger">optional logger for missing-judge warnings</param>
    public Evaluator(
        IReadOnlyDictionary<string, Judge> judges,
        LdAiConfigTypes.JudgeConfiguration judgeConfiguration,
        ILogger logger = null)
    {
        _judges = judges ?? new Dictionary<string, Judge>();
        _judgeConfiguration = judgeConfiguration;
        _logger = logger;
        _isNoop = false;
        _random = new Random();
    }

    /// <summary>
    /// Returns an <see cref="Evaluator"/> that performs no evaluation. <see cref="EvaluateAsync"/>
    /// on the returned instance resolves immediately to an empty list without invoking any judges
    /// or emitting any warnings.
    /// </summary>
    public static Evaluator Noop() => _noop;

    /// <summary>
    /// Runs all configured judges against the given input/output pair.
    /// </summary>
    /// <param name="input">the original input (e.g. user prompt)</param>
    /// <param name="output">the model response to evaluate</param>
    /// <returns>a list of <see cref="JudgeResult"/> values, one per judge that was found and
    /// executed; judges that are missing from the <c>judges</c> dictionary are skipped and
    /// produce no entry</returns>
    public async Task<IReadOnlyList<JudgeResult>> EvaluateAsync(string input, string output)
    {
        if (_isNoop || _judgeConfiguration == null || _judgeConfiguration.Judges.Count == 0)
        {
            return new List<JudgeResult>();
        }

        var results = new List<JudgeResult>(_judgeConfiguration.Judges.Count);
        foreach (var judgeEntry in _judgeConfiguration.Judges)
        {
            if (!_judges.TryGetValue(judgeEntry.Key, out var judge))
            {
                _logger?.Warn("Evaluator: judge '{0}' not found; skipping", judgeEntry.Key);
                continue;
            }

            var result = await judge.EvaluateAsync(input, output, judgeEntry.SamplingRate, _random);
            results.Add(result);
        }

        return results;
    }
}
