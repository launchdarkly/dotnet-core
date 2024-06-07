
using static LaunchDarkly.Sdk.Client.DataModel;
using static LaunchDarkly.Sdk.Client.Subsystems.EventProcessorTypes;

namespace LaunchDarkly.Sdk.Client.Internal.Events
{
    internal sealed class EventFactory
    {
        private readonly bool _withReasons;

        internal static readonly EventFactory Default = new EventFactory(false);
        internal static readonly EventFactory DefaultWithReasons = new EventFactory(true);

        internal EventFactory(bool withReasons)
        {
            _withReasons = withReasons;
        }

        internal EvaluationEvent NewEvaluationEvent(
            string flagKey,
            FeatureFlag flag,
            Context context,
            EvaluationDetail<LdValue> result,
            LdValue defaultValue
            )
        {
            // EventFactory passes the reason parameter to this method because the server-side SDK needs to
            // look at the reason; but in this client-side SDK, we don't look at that parameter, because
            // LD has already done the relevant calculation for us and sent us the result in trackReason.
            var isExperiment = flag.TrackReason;

            return new EvaluationEvent
            {
                Timestamp = UnixMillisecondTime.Now,
                Context = context,
                FlagKey = flagKey,
                FlagVersion = flag.FlagVersion ?? flag.Version,
                Variation = result.VariationIndex,
                Value = result.Value,
                Default = defaultValue,
                Reason = (_withReasons || isExperiment) ? result.Reason : (EvaluationReason?)null,
                TrackEvents = flag.TrackEvents || isExperiment,
                DebugEventsUntilDate = flag.DebugEventsUntilDate
            };
        }

        internal EvaluationEvent NewDefaultValueEvaluationEvent(
            string flagKey,
            FeatureFlag flag,
            Context context,
            LdValue defaultValue,
            EvaluationErrorKind errorKind
            )
        {
            return new EvaluationEvent
            {
                Timestamp = UnixMillisecondTime.Now,
                Context = context,
                FlagKey = flagKey,
                FlagVersion = flag.FlagVersion ?? flag.Version,
                Value = defaultValue,
                Default = defaultValue,
                Reason = _withReasons ? EvaluationReason.ErrorReason(errorKind) : (EvaluationReason?)null,
                TrackEvents = flag.TrackEvents,
                DebugEventsUntilDate = flag.DebugEventsUntilDate
            };
        }

        internal EvaluationEvent NewUnknownFlagEvaluationEvent(
            string flagKey,
            Context context,
            LdValue defaultValue,
            EvaluationErrorKind errorKind
            )
        {
            return new EvaluationEvent
            {
                Timestamp = UnixMillisecondTime.Now,
                Context = context,
                FlagKey = flagKey,
                Value = defaultValue,
                Default = defaultValue,
                Reason = _withReasons ? EvaluationReason.ErrorReason(errorKind) : (EvaluationReason?)null,
            };
        }
    }
}
