using System;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Client.Hooks;
using LaunchDarkly.Sdk.Client.Internal.Hooks.Interfaces;

namespace LaunchDarkly.Sdk.Client.Internal.Hooks.Executor
{
    /// <summary>
    /// NoopExecutor does not execute any hook logic. It may be used to avoid any overhead associated with
    /// hook execution if the SDK is configured without hooks.
    /// </summary>
    internal sealed class NoopExecutor : IHookExecutor
    {
        public EvaluationDetail<T> EvaluationSeries<T>(EvaluationSeriesContext context,
            LdValue.Converter<T> converter, Func<EvaluationDetail<T>> evaluate) => evaluate();

        public async Task<bool> IdentifySeries(Context context, TimeSpan maxWaitTime, Func<Task<bool>> identify)
        {
            try
            {
                return await identify();
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void Dispose()
        {
        }
    }
}
