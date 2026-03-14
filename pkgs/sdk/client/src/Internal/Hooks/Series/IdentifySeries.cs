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

    internal class IdentifyStage
    {
        protected enum Stage
        {
            BeforeIdentify,
            AfterIdentify
        }

        protected readonly EvaluationStage.Order _order;
        private readonly Logger _logger;

        protected IdentifyStage(Logger logger, EvaluationStage.Order order)
        {
            _logger = logger;
            _order = order;
        }

        protected void LogFailure(IdentifySeriesContext context, Hook h, Stage stage, Exception e)
        {
            _logger.Error("During identify of context \"{0}\", stage \"{1}\" of hook \"{2}\" reported error: {3}",
                context.Context.Key, stage.ToString(), h.Metadata.Name, e.Message);
        }
    }

    internal sealed class BeforeIdentify : IdentifyStage, IStageExecutor<IdentifySeriesContext>
    {
        private readonly IEnumerable<Hook> _hooks;

        public BeforeIdentify(Logger logger, IEnumerable<Hook> hooks, EvaluationStage.Order order) : base(logger, order)
        {
            _hooks = (order == EvaluationStage.Order.Forward) ? hooks : hooks.Reverse();
        }

        public IEnumerable<SeriesData> Execute(IdentifySeriesContext context, IEnumerable<SeriesData> _)
        {
            return _hooks.Select(hook =>
            {
                try
                {
                    return hook.BeforeIdentify(context, SeriesData.Empty);
                }
                catch (Exception e)
                {
                    LogFailure(context, hook, Stage.BeforeIdentify, e);
                    return SeriesData.Empty;
                }
            }).ToList();
        }
    }

    internal sealed class AfterIdentify : IdentifyStage, IStageExecutor<IdentifySeriesContext, IdentifySeriesResult>
    {
        private readonly IEnumerable<Hook> _hooks;

        public AfterIdentify(Logger logger, IEnumerable<Hook> hooks, EvaluationStage.Order order) : base(logger, order)
        {
            _hooks = (order == EvaluationStage.Order.Forward) ? hooks : hooks.Reverse();
        }

        public IEnumerable<SeriesData> Execute(IdentifySeriesContext context, IdentifySeriesResult result,
            IEnumerable<SeriesData> seriesData)
        {
            return _hooks.Zip((_order == EvaluationStage.Order.Reverse ? seriesData.Reverse() : seriesData), (hook, data) =>
            {
                try
                {
                    return hook.AfterIdentify(context, data, result);
                }
                catch (Exception e)
                {
                    LogFailure(context, hook, Stage.AfterIdentify, e);
                    return SeriesData.Empty;
                }
            }).ToList();
        }
    }
}
