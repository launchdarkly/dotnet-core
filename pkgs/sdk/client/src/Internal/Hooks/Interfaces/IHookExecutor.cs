using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Client.Hooks;

namespace LaunchDarkly.Sdk.Client.Internal.Hooks.Interfaces
{
    /// <summary>
    /// An IHookExecutor is responsible for executing the logic contained in a series of hook stages.
    /// </summary>
    internal interface IHookExecutor : IDisposable
    {
        /// <summary>
        /// Runs the evaluation series for each configured hook, invoking the <paramref name="evaluate"/>
        /// delegate to obtain the flag value. Exceptions thrown by the delegate are propagated to the caller.
        /// </summary>
        /// <param name="context">context for the evaluation series</param>
        /// <param name="converter">used to convert the primitive evaluation value into a wrapped <see cref="LdValue"/> suitable for use in hooks</param>
        /// <param name="evaluate">function to evaluate the flag value</param>
        /// <typeparam name="T">primitive type of the flag value</typeparam>
        /// <returns>the EvaluationDetail returned from the evaluator</returns>
        EvaluationDetail<T> EvaluationSeries<T>(EvaluationSeriesContext context,
            LdValue.Converter<T> converter, Func<EvaluationDetail<T>> evaluate);

        /// <summary>
        /// Runs the identify series for each configured hook, invoking the <paramref name="identify"/>
        /// delegate to perform the identify operation. Exceptions thrown by the delegate are propagated
        /// to the caller.
        /// </summary>
        /// <param name="context">the evaluation context being identified</param>
        /// <param name="maxWaitTime">the timeout for the identify operation</param>
        /// <param name="identify">async function that performs the identify operation</param>
        /// <returns>the result of the identify operation</returns>
        Task<bool> IdentifySeries(Context context, TimeSpan maxWaitTime, Func<Task<bool>> identify);

        /// <summary>
        /// Adds additional hooks to the executor so that subsequent <see cref="EvaluationSeries{T}"/> and
        /// <see cref="IdentifySeries"/> calls invoke them. The implementation is responsible for
        /// synchronizing concurrent calls.
        /// </summary>
        /// <param name="hooks">the hooks to add; may be empty or null (no-op in either case)</param>
        void AddHooks(IEnumerable<Hook> hooks);
    }
}
