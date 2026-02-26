using System;
using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Client.Hooks;
using LaunchDarkly.Sdk.Client.Internal.Hooks.Interfaces;
using LaunchDarkly.Sdk.Client.Internal.Hooks.Series;

namespace LaunchDarkly.Sdk.Client.Internal.Hooks.Executor
{
    internal sealed class Executor : IHookExecutor
    {
        private readonly List<Hook> _hooks;

        private readonly IStageExecutor<EvaluationSeriesContext> _beforeEvaluation;
        private readonly IStageExecutor<EvaluationSeriesContext, EvaluationDetail<LdValue>> _afterEvaluation;

        public Executor(Logger logger, IEnumerable<Hook> hooks)
        {
            _hooks = hooks.ToList();
            _beforeEvaluation = new BeforeEvaluation(logger, _hooks, EvaluationStage.Order.Forward);
            _afterEvaluation = new AfterEvaluation(logger, _hooks, EvaluationStage.Order.Reverse);
        }

        public EvaluationDetail<T> EvaluationSeries<T>(EvaluationSeriesContext context,
            LdValue.Converter<T> converter, Func<EvaluationDetail<T>> evaluate)
        {
            var seriesData = _beforeEvaluation.Execute(context, default);

            var detail = evaluate();

            _afterEvaluation.Execute(context,
                new EvaluationDetail<LdValue>(converter.FromType(detail.Value), detail.VariationIndex, detail.Reason),
                seriesData);

            return detail;
        }

        public void Dispose()
        {
            foreach (var hook in _hooks)
            {
                hook?.Dispose();
            }
        }
    }
}
