using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;
using LaunchDarkly.Sdk.Server.Ai.Tracking;

namespace LaunchDarkly.Sdk.Server.Ai.Evals;

/// <summary>
/// Executes a judge evaluation against a model provider using a configured
/// <see cref="LdAiJudgeConfig"/> and an <see cref="IRunner"/>.
/// </summary>
public sealed class Judge
{
    private static readonly IReadOnlyDictionary<string, object> EvaluationSchema =
        new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["score"] = new Dictionary<string, object> { ["type"] = "number" },
                ["reasoning"] = new Dictionary<string, object> { ["type"] = "string" }
            },
            ["required"] = new[] { "score", "reasoning" },
            ["additionalProperties"] = false
        };

    /// <summary>The judge's AI config.</summary>
    public LdAiJudgeConfig Config { get; }

    /// <summary>The runner used to invoke the model.</summary>
    public IRunner Runner { get; }

    private readonly ILogger _logger;

    /// <summary>
    /// Constructs a <see cref="Judge"/>.
    /// </summary>
    /// <param name="config">the judge AI config</param>
    /// <param name="runner">the model runner</param>
    /// <param name="logger">optional logger for warnings</param>
    public Judge(LdAiJudgeConfig config, IRunner runner, ILogger logger = null)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        Runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _logger = logger;
    }

    /// <summary>
    /// Evaluates the given input/output pair using the judge's model.
    /// </summary>
    /// <param name="input">the original input (e.g. user prompt or message history)</param>
    /// <param name="output">the model response to evaluate</param>
    /// <param name="samplingRate">when provided, the fraction of requests to actually evaluate;
    /// if a random draw exceeds this value the evaluation is skipped</param>
    /// <returns>a <see cref="JudgeResult"/> describing the evaluation outcome</returns>
    public async Task<JudgeResult> EvaluateAsync(string input, string output,
        double? samplingRate = null)
    {
        if (string.IsNullOrWhiteSpace(Config.EvaluationMetricKey))
        {
            _logger?.Warn("Judge '{0}': missing evaluation metric key", Config.Key);
            return new JudgeResult(sampled: true, success: false, judgeConfigKey: Config.Key,
                errorMessage: "Judge configuration is missing required evaluation metric key");
        }

        var effectiveRate = samplingRate.HasValue ? NormalizeSamplingRate(samplingRate.Value) : 1.0;
        if (new Random().NextDouble() > effectiveRate)
        {
            return new JudgeResult(sampled: false, judgeConfigKey: Config.Key);
        }

        var formatted = $"MESSAGE HISTORY:\n{input}\n\nRESPONSE TO EVALUATE:\n{output}";
        var tracker = Config.CreateTracker();

        RunnerResult result;
        try
        {
            result = await tracker.TrackMetricsOf(
                r => r.Metrics,
                () => Runner.RunAsync(formatted, EvaluationSchema));
        }
        catch (Exception ex)
        {
            return new JudgeResult(Config.EvaluationMetricKey, 0,
                sampled: true, success: false, judgeConfigKey: Config.Key,
                errorMessage: ex.Message);
        }

        double score = 0;
        string reasoning = null;
        bool scoreExtracted = false;

        if (result?.Parsed != null)
        {
            if (result.Parsed.TryGetValue("score", out var rawScore))
            {
                try
                {
                    if (rawScore is System.Text.Json.JsonElement je &&
                        je.ValueKind == System.Text.Json.JsonValueKind.Number)
                    {
                        score = je.GetDouble();
                        scoreExtracted = true;
                    }
                    else
                    {
                        score = Convert.ToDouble(rawScore);
                        scoreExtracted = true;
                    }
                }
                catch { /* handled below */ }
            }

            if (result.Parsed.TryGetValue("reasoning", out var rawReasoning))
            {
                if (rawReasoning is string s)
                {
                    reasoning = s;
                }
                else
                {
                    _logger?.Warn(
                        "Judge '{0}': reasoning field is not a string; ignoring", Config.Key);
                }
            }
        }

        if (!scoreExtracted)
        {
            var noScoreMsg = "Runner did not return a valid score";
            _logger?.Warn("Judge '{0}': {1}", Config.Key, noScoreMsg);
            return new JudgeResult(Config.EvaluationMetricKey, 0,
                sampled: true, success: false, judgeConfigKey: Config.Key,
                errorMessage: noScoreMsg);
        }

        if (double.IsNaN(score) || double.IsInfinity(score) || score < 0.0 || score > 1.0)
        {
            var msg = $"Score {score} is out of range [0, 1]";
            _logger?.Warn("Judge '{0}': {1}", Config.Key, msg);
            return new JudgeResult(Config.EvaluationMetricKey, 0,
                sampled: true, success: false, judgeConfigKey: Config.Key,
                errorMessage: msg);
        }

        return new JudgeResult(Config.EvaluationMetricKey, score,
            sampled: true, success: true, judgeConfigKey: Config.Key,
            reasoning: reasoning);
    }

    /// <summary>
    /// Evaluates the given conversation messages and runner result using the judge's model.
    /// Messages are formatted as <c>"role: content\n..."</c> and the runner result's
    /// <see cref="RunnerResult.Content"/> is used as the output to evaluate.
    /// </summary>
    /// <param name="messages">the conversation messages</param>
    /// <param name="runnerResult">the runner result whose content is to be evaluated</param>
    /// <param name="samplingRate">optional sampling rate; see <see cref="EvaluateAsync"/></param>
    /// <returns>a <see cref="JudgeResult"/> describing the evaluation outcome</returns>
    public Task<JudgeResult> EvaluateMessagesAsync(
        IReadOnlyList<LdAiConfigTypes.Message> messages,
        RunnerResult runnerResult,
        double? samplingRate = null)
    {
        var formattedMessages = messages == null
            ? string.Empty
            : string.Join("\n", messages.Select(m => $"{m.Role.ToString().ToLowerInvariant()}: {m.Content}"));

        return EvaluateAsync(formattedMessages, runnerResult?.Content ?? string.Empty, samplingRate);
    }

    private static double NormalizeSamplingRate(double rate)
    {
        if (double.IsNaN(rate) || double.IsInfinity(rate)) return 1.0;
        if (rate < 0.0) return 0.0;
        if (rate > 1.0) return 1.0;
        return rate;
    }
}
