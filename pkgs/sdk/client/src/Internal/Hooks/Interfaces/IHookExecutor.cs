using System;
using LaunchDarkly.Sdk.Client.Hooks;

namespace LaunchDarkly.Sdk.Client.Internal.Hooks.Interfaces
{
    /// <summary>
    /// An IHookExecutor is responsible for executing the logic contained in a series of hook stages.
    ///
    /// The purpose of this interface is to allow the SDK to swap out the executor based on having any hooks
    /// configured or not. If there are no hooks, the interface methods can be no-ops.
    /// </summary>
    internal interface IHookExecutor : IDisposable
    {
        /// <summary>
        /// EvaluationSeries should run the evaluation series for each configured hook.
        /// </summary>
        /// <param name="context">context for the evaluation series</param>
        /// <param name="converter">used to convert the primitive evaluation value into a wrapped <see cref="LdValue"/> suitable for use in hooks</param>
        /// <param name="evaluate">function to evaluate the flag value</param>
        /// <typeparam name="T">primitive type of the flag value</typeparam>
        /// <returns>the EvaluationDetail returned from the evaluator</returns>
        EvaluationDetail<T> EvaluationSeries<T>(EvaluationSeriesContext context,
            LdValue.Converter<T> converter, Func<EvaluationDetail<T>> evaluate);
    }
}
