using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Client.Hooks;
using LaunchDarkly.Sdk.Client.Internal.Hooks.Interfaces;
using LaunchDarkly.Sdk.Client.Internal.Hooks.Series;

namespace LaunchDarkly.Sdk.Client.Internal.Hooks.Executor
{
    internal sealed class Executor : IHookExecutor
    {
        private readonly List<Hook> _hooks;
        private readonly Logger _logger;

        private readonly IStageExecutor<EvaluationSeriesContext> _beforeEvaluation;
        private readonly IStageExecutor<EvaluationSeriesContext, EvaluationDetail<LdValue>> _afterEvaluation;

        private readonly IStageExecutor<IdentifySeriesContext> _beforeIdentify;
        private readonly IStageExecutor<IdentifySeriesContext, IdentifySeriesResult> _afterIdentify;

        public Executor(Logger logger, IEnumerable<Hook> hooks)
        {
            _logger = logger;
            _hooks = hooks.ToList();
            _beforeEvaluation = new BeforeEvaluation(logger, _hooks, EvaluationStage.Order.Forward);
            _afterEvaluation = new AfterEvaluation(logger, _hooks, EvaluationStage.Order.Reverse);
            _beforeIdentify = new BeforeIdentify(logger, _hooks, EvaluationStage.Order.Forward);
            _afterIdentify = new AfterIdentify(logger, _hooks, EvaluationStage.Order.Reverse);
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

        public async Task<bool> IdentifySeries(Context context, TimeSpan maxWaitTime, Func<Task<bool>> identify)
        {
            var identifyContext = new IdentifySeriesContext(context, maxWaitTime);
            var seriesData = _beforeIdentify.Execute(identifyContext, default);

            try
            {
                var result = await identify();

                _afterIdentify.Execute(identifyContext,
                    new IdentifySeriesResult(IdentifySeriesResult.IdentifySeriesStatus.Completed),
                    seriesData);

                return result;
            }
            catch (Exception)
            {
                _afterIdentify.Execute(identifyContext,
                    new IdentifySeriesResult(IdentifySeriesResult.IdentifySeriesStatus.Error),
                    seriesData);

                return false;
            }
        }

        public void Dispose()
        {
            foreach (var hook in _hooks)
            {
                try
                {
                    hook?.Dispose();
                }
                catch (Exception e)
                {
                    _logger.Error("During disposal of hook \"{0}\" reported error: {1}",
                        hook?.Metadata.Name, e.Message);
                }
            }
        }
    }
}
