using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Client.Hooks;
using LaunchDarkly.Sdk.Client.Internal.Hooks.Interfaces;

namespace LaunchDarkly.Sdk.Client.Internal.Hooks.Series
{
    using SeriesData = ImmutableDictionary<string, object>;

    internal class EvaluationStage
    {
        public enum Order
        {
            Forward,
            Reverse
        }

        protected enum Stage
        {
            BeforeEvaluation,
            AfterEvaluation
        }

        protected readonly Order _order;
        private readonly Logger _logger;

        protected EvaluationStage(Logger logger, Order order)
        {
            _logger = logger;
            _order = order;
        }

        protected void LogFailure(EvaluationSeriesContext context, Hook h, Stage stage, Exception e)
        {
            _logger.Error("During evaluation of flag \"{0}\", stage \"{1}\" of hook \"{2}\" reported error: {3}",
                context.FlagKey, stage.ToString(), h.Metadata.Name, e.Message);
        }
    }

    internal sealed class BeforeEvaluation : EvaluationStage, IStageExecutor<EvaluationSeriesContext>
    {
        private readonly IEnumerable<Hook> _hooks;

        public BeforeEvaluation(Logger logger, IEnumerable<Hook> hooks, Order order) : base(logger, order)
        {
            _hooks = (order == Order.Forward) ? hooks : hooks.Reverse();
        }

        public IEnumerable<SeriesData> Execute(EvaluationSeriesContext context, IEnumerable<SeriesData> _)
        {
            return _hooks.Select(hook =>
            {
                try
                {
                    return hook.BeforeEvaluation(context, SeriesData.Empty);
                }
                catch (Exception e)
                {
                    LogFailure(context, hook, Stage.BeforeEvaluation, e);
                    return SeriesData.Empty;
                }
            }).ToList();
        }
    }

    internal sealed class AfterEvaluation : EvaluationStage, IStageExecutor<EvaluationSeriesContext, EvaluationDetail<LdValue>>
    {
        private readonly IEnumerable<Hook> _hooks;

        public AfterEvaluation(Logger logger, IEnumerable<Hook> hooks, Order order) : base(logger, order)
        {
            _hooks = (order == Order.Forward) ? hooks : hooks.Reverse();
        }

        public IEnumerable<SeriesData> Execute(EvaluationSeriesContext context, EvaluationDetail<LdValue> detail,
            IEnumerable<SeriesData> seriesData)
        {
            return _hooks.Zip((_order == Order.Reverse ? seriesData.Reverse() : seriesData), (hook, data) =>
            {
                try
                {
                    return hook.AfterEvaluation(context, data, detail);
                }
                catch (Exception e)
                {
                    LogFailure(context, hook, Stage.AfterEvaluation, e);
                    return SeriesData.Empty;
                }
            }).ToList();
        }
    }
}
