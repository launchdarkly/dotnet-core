using System.Collections.Generic;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Ai.Evals;

namespace LaunchDarkly.Sdk.Server.Ai.Interfaces;

/// <summary>
/// Represents a runner that can execute a prompt against a model provider and return a result.
/// </summary>
public interface IRunner
{
    /// <summary>
    /// Executes the given input against the model provider.
    /// </summary>
    /// <param name="input">the prompt text to send to the model</param>
    /// <param name="outputType">optional JSON schema for structured output; when <c>null</c> the runner
    /// returns free-form text</param>
    /// <returns>the result of the model invocation</returns>
    Task<RunnerResult> RunAsync(string input,
        IReadOnlyDictionary<string, object> outputType = null);
}
