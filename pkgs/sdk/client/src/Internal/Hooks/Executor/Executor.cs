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
        // Immutable bundle of per-generation state. Once a Stages instance is published via the
        // volatile _stages field, none of its fields (including the contents of Hooks) are
        // mutated. AddHooks always builds a fresh Stages over a fresh List<Hook>.
        private sealed class Stages
        {
            public readonly List<Hook> Hooks;
            public readonly IStageExecutor<EvaluationSeriesContext> BeforeEvaluation;
            public readonly IStageExecutor<EvaluationSeriesContext, EvaluationDetail<LdValue>> AfterEvaluation;
            public readonly IStageExecutor<IdentifySeriesContext> BeforeIdentify;
            public readonly IStageExecutor<IdentifySeriesContext, IdentifySeriesResult> AfterIdentify;

            public Stages(Logger logger, List<Hook> hooks)
            {
                Hooks = hooks;
                BeforeEvaluation = new BeforeEvaluation(logger, hooks, EvaluationStage.Order.Forward);
                AfterEvaluation = new AfterEvaluation(logger, hooks, EvaluationStage.Order.Reverse);
                BeforeIdentify = new BeforeIdentify(logger, hooks, EvaluationStage.Order.Forward);
                AfterIdentify = new AfterIdentify(logger, hooks, EvaluationStage.Order.Reverse);
            }
        }

        private readonly Logger _logger;
        private readonly object _lock = new object();
        private volatile Stages _stages;

        public Executor(Logger logger, IEnumerable<Hook> hooks)
        {
            _logger = logger;
            _stages = new Stages(logger, hooks.ToList());
        }

        public void AddHooks(IEnumerable<Hook> hooks)
        {
            if (hooks == null) return;
            lock (_lock)
            {
                var added = hooks.ToList();
                if (added.Count == 0) return;
                var current = _stages.Hooks;
                var newHooks = new List<Hook>(current.Count + added.Count);
                newHooks.AddRange(current);
                newHooks.AddRange(added);
                _stages = new Stages(_logger, newHooks);
            }
        }

        public EvaluationDetail<T> EvaluationSeries<T>(EvaluationSeriesContext context,
            LdValue.Converter<T> converter, Func<EvaluationDetail<T>> evaluate)
        {
            // Snapshot the stages once so Before/After see a single, consistent generation
            // even if AddHooks publishes a new Stages mid-call.
            var stages = _stages;
            if (stages.Hooks.Count == 0) return evaluate();

            var seriesData = stages.BeforeEvaluation.Execute(context, default);

            var detail = evaluate();

            stages.AfterEvaluation.Execute(context,
                new EvaluationDetail<LdValue>(converter.FromType(detail.Value), detail.VariationIndex, detail.Reason),
                seriesData);

            return detail;
        }

        public async Task<bool> IdentifySeries(Context context, TimeSpan maxWaitTime, Func<Task<bool>> identify)
        {
            var stages = _stages;
            if (stages.Hooks.Count == 0) return await identify();

            var identifyContext = new IdentifySeriesContext(context, maxWaitTime);
            var seriesData = stages.BeforeIdentify.Execute(identifyContext, default);

            try
            {
                var result = await identify();

                stages.AfterIdentify.Execute(identifyContext,
                    new IdentifySeriesResult(IdentifySeriesResult.IdentifySeriesStatus.Completed),
                    seriesData);

                return result;
            }
            catch (Exception)
            {
                stages.AfterIdentify.Execute(identifyContext,
                    new IdentifySeriesResult(IdentifySeriesResult.IdentifySeriesStatus.Error),
                    seriesData);

                throw;
            }
        }

        public void Dispose()
        {
            var stages = _stages;
            foreach (var hook in stages.Hooks)
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
