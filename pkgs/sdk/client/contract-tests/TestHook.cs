using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using LaunchDarkly.Sdk;
using LaunchDarkly.Sdk.Client.Hooks;
using LaunchDarkly.Sdk.Client.Interfaces;
using LaunchDarkly.Sdk.Client.Plugins;
using LaunchDarkly.Sdk.Integrations.Plugins;

namespace TestService
{
    using SeriesData = ImmutableDictionary<string, object>;

    // The client SDK only takes hooks from plugins, so the test hooks are registered through this one.
    public class TestHookPlugin : Plugin
    {
        private readonly IList<Hook> _hooks;

        public TestHookPlugin(IList<Hook> hooks) : base("contract-test-hooks")
        {
            _hooks = hooks;
        }

        public override void Register(ILdClient client, EnvironmentMetadata metadata)
        {
        }

        public override IList<Hook> GetHooks(EnvironmentMetadata metadata) => _hooks;
    }

    public class TestHook : Hook
    {
        private readonly CallbackService _service;
        private readonly Dictionary<string, LdValue> _before;
        private readonly Dictionary<string, LdValue> _after;

        private readonly string _beforeError;
        private readonly string _afterError;
        private readonly bool _evaluationReasons;

        public TestHook(string name, CallbackService service, Dictionary<string, LdValue> before, Dictionary<string, LdValue> after, string beforeError, string afterError, bool evaluationReasons) : base(name)
        {
            _service = service;
            _before = before;
            _after = after;
            _beforeError = beforeError;
            _afterError = afterError;
            _evaluationReasons = evaluationReasons;
        }

        public override SeriesData BeforeEvaluation(EvaluationSeriesContext context, SeriesData data)
        {
            if (_beforeError != null)
            {
                throw new Exception(_beforeError);
            }

            _service.Post("", new EvaluationHookParams()
            {
                EvaluationSeriesContext = context,
                EvaluationSeriesData = data,
                Stage = "beforeEvaluation"
            });

            if (_before == null) return base.BeforeEvaluation(context, data);
            var builder = data.ToBuilder();
            foreach (var entry in _before)
            {
                builder[entry.Key] = entry.Value;
            }

            return builder.ToImmutable();
        }

        public override SeriesData AfterEvaluation(EvaluationSeriesContext context, SeriesData data, EvaluationDetail<LdValue> detail)
        {
            if (_afterError != null)
            {
                throw new Exception(_afterError);
            }

            // Report the reason the same way SdkClientEntity reports evaluation results: without
            // evaluation reasons the SDK fills in an OFF reason, which the harness expects to be absent.
            var reason = !_evaluationReasons && detail.Reason.Kind == EvaluationReasonKind.Off
                ? (EvaluationReason?)null
                : detail.Reason;

            _service.Post("", new EvaluationHookParams()
            {
                EvaluationSeriesContext = context,
                EvaluationSeriesData = data,
                EvaluationDetail = new EvaluateFlagResponse()
                {
                    Reason = reason,
                    VariationIndex = detail.VariationIndex,
                    Value = detail.Value
                },
                Stage = "afterEvaluation"
            });

            if (_after == null) return base.AfterEvaluation(context, data, detail);
            var builder = data.ToBuilder();
            foreach (var entry in _after)
            {
                builder[entry.Key] = entry.Value;
            }

            return builder.ToImmutable();
        }
    }
}
