using System.Text.Json.Serialization;
using LaunchDarkly.Sdk.Json;

namespace LaunchDarkly.Sdk
{
    /// <summary>
    /// An object returned by the "variation detail" methods of the client, combining the result
    /// of a flag evaluation with an explanation of how it was calculated.
    /// </summary>
    /// <typeparam name="T">the type of the flag value</typeparam>
    public struct EvaluationDetail<T>
    {
        private readonly T _value;
        private readonly int? _variationIndex;
        private readonly EvaluationReason _reason;

        /// <summary>
        /// The result of the flag evaluation. This will be either one of the flag's variations or the default
        /// value that was specified when the flag was evaluated.
        /// </summary>
        public T Value => _value;

        /// <summary>
        /// The index of the returned value within the flag's list of variations, e.g. 0 for the first variation -
        /// or <see langword="null"/> if the default value was returned.
        /// </summary>
        public int? VariationIndex => _variationIndex;

        /// <summary>
        /// An object describing the main factor that influenced the flag evaluation value.
        /// </summary>
        public EvaluationReason Reason => _reason;

        /// <summary>
        /// True if the flag evaluated to the default value, rather than one of its variations.
        /// </summary>
        public bool IsDefaultValue => _variationIndex == null;

        /// <summary>
        /// Constructs a new EvaluationDetail insetance.
        /// </summary>
        /// <param name="value">the flag value</param>
        /// <param name="variationIndex">the variation index</param>
        /// <param name="reason">the evaluation reason</param>
        public EvaluationDetail(T value, int? variationIndex, EvaluationReason reason)
        {
            _value = value;
            _variationIndex = variationIndex;
            _reason = reason;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is EvaluationDetail<T> o &&
                (Value == null ? o.Value == null : Value.Equals(o.Value))
                    && VariationIndex == o.VariationIndex && Reason.Equals(o.Reason);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            new HashCodeBuilder().With(Value).With(VariationIndex).With(Reason).Value;
    }

    /// <summary>
    /// Describes the reason that a flag evaluation produced a particular value.
    /// </summary>
    /// <remarks>
    /// For converting this type to or from JSON, see <see cref="LaunchDarkly.Sdk.Json"/>.
    /// </remarks>
    [JsonConverter(typeof(LdJsonConverters.EvaluationReasonConverter))]
    public struct EvaluationReason : IJsonSerializable
    {
        private static readonly EvaluationReason _offInstance =
            new EvaluationReason(EvaluationReasonKind.Off, null, null, null, null, false, null);
        private static readonly EvaluationReason _fallthroughInstance =
            new EvaluationReason(EvaluationReasonKind.Fallthrough, null, null, null, null, false, null);
        private static readonly EvaluationReason _targetMatchInstance =
            new EvaluationReason(EvaluationReasonKind.TargetMatch, null, null, null, null, false, null);

        private readonly EvaluationReasonKind _kind;
        private readonly int? _ruleIndex;
        private readonly string _ruleId;
        private readonly string _prerequisiteKey;
        private readonly EvaluationErrorKind? _errorKind;
        private readonly bool _inExperiment;
        private readonly BigSegmentsStatus? _bigSegmentsStatus;

        /// <summary>
        /// An enum indicating the general category of the reason.
        /// </summary>
        public EvaluationReasonKind Kind => _kind;

        /// <summary>
        /// The index of the rule that was matched (0 for the first), or <see langword="null"/> if this is not a rule match.
        /// </summary>
        public int? RuleIndex => _ruleIndex;

        /// <summary>
        /// The unique identifier of the rule that was matched, or <see langword="null"/> if this is not a rule match.
        /// </summary>
        public string RuleId => _ruleId;

        /// <summary>
        /// The key of the prerequisite flag that failed, if <see cref="Kind"/> is <see cref="EvaluationReasonKind.PrerequisiteFailed"/>,
        /// otherwise <see langword="null"/>.
        /// </summary>
        public string PrerequisiteKey => _prerequisiteKey;

        /// <summary>
        /// Describes the type of error, if <see cref="Kind"/> is <see cref="EvaluationReasonKind.Error"/>, otherwise
        /// <see langword="null"/>.
        /// </summary>
        public EvaluationErrorKind? ErrorKind => _errorKind;

        /// <summary>
        /// Whether the evaluation was part of an experiment.
        /// </summary>
        /// <remarks>
        /// This is true if the evaluation resulted in an experiment rollout and served one of the
        /// variations in the experiment. Otherwise it is false.
        /// </remarks>
        public bool InExperiment => _inExperiment;

        /// <summary>
        /// Describes the validity of big segment information, if and only if the flag evaluation required querying
        /// at least one big segment. Otherwise it returns <see langword="null"/>.
        /// </summary>
        /// <remarks>
        /// "Big segments" are a specific kind of user segments. For more information, read the LaunchDarkly
        /// documentation about user segments: https://docs.launchdarkly.com/home/users/big-segments
        /// </remarks>
        public BigSegmentsStatus? BigSegmentsStatus => _bigSegmentsStatus;

        internal EvaluationReason(
            EvaluationReasonKind kind,
            int? ruleIndex,
            string ruleId,
            string prereqKey,
            EvaluationErrorKind? errorKind,
            bool inExperiment,
            BigSegmentsStatus? bigSegmentsStatus
            )
        {
            _kind = kind;
            _ruleIndex = ruleIndex;
            _ruleId = ruleId;
            _prerequisiteKey = prereqKey;
            _errorKind = errorKind;
            _inExperiment = inExperiment;
            _bigSegmentsStatus = bigSegmentsStatus;
        }

        /// <summary>
        /// Returns an EvaluationReason of the kind <see cref="EvaluationReasonKind.Off"/>.
        /// </summary>
        public static EvaluationReason OffReason => _offInstance;

        /// <summary>
        /// Returns an EvaluationReason of the kind <see cref="EvaluationReasonKind.Fallthrough"/>.
        /// </summary>
        public static EvaluationReason FallthroughReason => _fallthroughInstance;

        /// <summary>
        /// Returns an EvaluationReason of the kind <see cref="EvaluationReasonKind.TargetMatch"/>.
        /// </summary>
        public static EvaluationReason TargetMatchReason => _targetMatchInstance;

        /// <summary>
        /// Returns an EvaluationReason of the kind <see cref="EvaluationReasonKind.RuleMatch"/>.
        /// </summary>
        /// <param name="ruleIndex">the rule index</param>
        /// <param name="ruleId">the unique rule ID</param>
        /// <returns>a reason descriptor</returns>
        public static EvaluationReason RuleMatchReason(int ruleIndex, string ruleId) =>
            new EvaluationReason(EvaluationReasonKind.RuleMatch, ruleIndex, ruleId, null, null, false, null);

        /// <summary>
        /// Returns an EvaluationReason of the kind <see cref="EvaluationReasonKind.PrerequisiteFailed"/>.
        /// </summary>
        /// <param name="key">the key of the prerequisite flag</param>
        /// <returns>a reason descriptor</returns>
        public static EvaluationReason PrerequisiteFailedReason(string key) =>
            new EvaluationReason(EvaluationReasonKind.PrerequisiteFailed, null, null, key, null, false, null);

        /// <summary>
        /// Returns an EvaluationReason of the kind <see cref="EvaluationReasonKind.Error"/>.
        /// </summary>
        /// <param name="errorKind"></param>
        /// <returns>a reason descriptor</returns>
        public static EvaluationReason ErrorReason(EvaluationErrorKind errorKind) =>
            new EvaluationReason(EvaluationReasonKind.Error, null, null, null, errorKind, false, null);

        /// <summary>
        /// Returns a copy of this EvaluationReason with a specific <see cref="BigSegmentsStatus"/> value added.
        /// </summary>
        /// <param name="bigSegmentsStatus">the new property value</param>
        /// <returns>a reason descriptor</returns>
        public EvaluationReason WithBigSegmentsStatus(BigSegmentsStatus? bigSegmentsStatus) =>
            new EvaluationReason(_kind, _ruleIndex, _ruleId, _prerequisiteKey, _errorKind,
                _inExperiment, bigSegmentsStatus);

        /// <summary>
        /// Returns a new instance with the <see cref="InExperiment"/> property set to the specified
        /// value, if supported.
        /// </summary>
        /// <remarks>
        /// Setting <see cref="InExperiment"/> is only allowed for <see cref="EvaluationReasonKind.Fallthrough"/>
        /// and <see cref="EvaluationReasonKind.RuleMatch"/>. For all other reason kinds, this has no effect.
        /// </remarks>
        /// <param name="inExperiment">the desired value for the property</param>
        /// <returns>a copy of this instance with the property modified</returns>
        public EvaluationReason WithInExperiment(bool inExperiment)
        {
            switch (_kind)
            {
                case EvaluationReasonKind.Fallthrough:
                case EvaluationReasonKind.RuleMatch:
                    return new EvaluationReason(_kind, _ruleIndex, _ruleId, _prerequisiteKey, _errorKind, inExperiment, _bigSegmentsStatus);
                default:
                    return this;
            }
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is EvaluationReason o &&
                _kind == o._kind && _ruleId == o._ruleId && _ruleIndex == o._ruleIndex &&
                    _prerequisiteKey == o._prerequisiteKey && _errorKind == o._errorKind &&
                    _inExperiment == o._inExperiment && _bigSegmentsStatus == o._bigSegmentsStatus;

        /// <inheritdoc/>
        public override int GetHashCode() =>
            new HashCodeBuilder().With(_kind).With(_ruleIndex).With(_ruleId).With(_prerequisiteKey)
                .With(_errorKind).With(_inExperiment).With(_bigSegmentsStatus).Value;

        /// <inheritdoc/>
        public override string ToString()
        {
            var kindStr = LdJsonConverters.EvaluationReasonKindConverter.ToIdentifier(_kind);
            switch (_kind)
            {
                case EvaluationReasonKind.RuleMatch:
                    return kindStr + "(" + _ruleIndex + "," + _ruleId + ")";
                case EvaluationReasonKind.PrerequisiteFailed:
                    return kindStr + "(" + _prerequisiteKey + ")";
                case EvaluationReasonKind.Error:
                    return kindStr + "(" + LdJsonConverters.EvaluationErrorKindConverter.ToIdentifier(_errorKind.Value) + ")";
            }
            return kindStr;
        }
    }

    /// <summary>
    /// Enumerated type defining the possible values of <see cref="EvaluationReason.Kind"/>.
    /// </summary>
    /// <remarks>
    /// The JSON representation of this type, as used in LaunchDarkly analytics event data, uses
    /// uppercase strings with underscores (<c>"RULE_MATCH"</c> rather than <c>"RuleMatch"</c>).
    /// </remarks>
    [JsonConverter(typeof(LdJsonConverters.EvaluationReasonKindConverter))]
    public enum EvaluationReasonKind
    {
        /// <summary>
        /// Indicates that the flag was off and therefore returned its configured off value.
        /// </summary>
        Off,

        /// <summary>
        /// Indicates that the flag was on but the user did not match any targets or rules.
        /// </summary>
        Fallthrough,

        /// <summary>
        /// Indicates that the user key was specifically targeted for this flag.
        /// </summary>
        TargetMatch,

        /// <summary>
        /// Indicates that the user matched one of the flag's rules.
        /// </summary>
        RuleMatch,

        /// <summary>
        /// Indicates that the flag was considered off because it had at least one prerequisite flag
        /// that either was off or did not return the desired variation.
        /// </summary>
        PrerequisiteFailed,

        /// <summary>
        /// Indicates that the flag could not be evaluated, e.g. because it does not exist or due to an unexpected
        /// error. In this case the result value will be the default value that the caller passed to the client.
        /// </summary>
        Error
    }

    /// <summary>
    /// Enumerated type defining the possible values of <see cref="EvaluationReason.ErrorKind"/>.
    /// </summary>
    /// <remarks>
    /// The JSON representation of this type, as used in LaunchDarkly analytics event data, uses
    /// uppercase strings with underscores (<c>"FLAG_NOT_FOUND"</c> rather than <c>"FlagNotFound"</c>).
    /// </remarks>
    [JsonConverter(typeof(LdJsonConverters.EvaluationErrorKindConverter))]
    public enum EvaluationErrorKind
    {
        /// <summary>
        /// Indicates that the caller tried to evaluate a flag before the client had successfully initialized.
        /// </summary>
        ClientNotReady,

        /// <summary>
        /// Indicates that the caller provided a flag key that did not match any known flag.
        /// </summary>
        FlagNotFound,

        /// <summary>
        /// Indicates that the caller passed <see langword="null"/> for the user parameter, or the user lacked a key.
        /// </summary>
        UserNotSpecified,

        /// <summary>
        /// Indicates that there was an internal inconsistency in the flag data, e.g. a rule specified a nonexistent
        /// variation. An error message will always be logged in this case.
        /// </summary>
        MalformedFlag,

        /// <summary>
        /// Indicates that the result value was not of the requested type, e.g. you requested a <see langword="bool"/>
        /// but the value was an <see langword="int"/>.
        /// </summary>
        WrongType,

        /// <summary>
        /// Indicates that an unexpected exception stopped flag evaluation; check the log for details.
        /// </summary>
        Exception
    }

    /// <summary>
    /// Defines the possible values of <see cref="EvaluationReason.BigSegmentsStatus"/>.
    /// </summary>
    [JsonConverter(typeof(LdJsonConverters.BigSegmentsStatusConverter))]
    public enum BigSegmentsStatus
    {
        /// <summary>
        /// Indicates that the big segment query involved in the flag evaluation was successful, and
        /// that the segment state is considered up to date.
        /// </summary>
        Healthy,

        /// <summary>
        /// Indicates that the big segment query involved in the flag evaluation was successful, but
        /// that the segment state may not be up to date.
        /// </summary>
        Stale,

        /// <summary>
        /// Indicates that big segments could not be queried for the flag evaluation because the SDK
        /// configuration did not include a big segment store.
        /// </summary>
        NotConfigured,

        /// <summary>
        /// Indicates that the big segment query involved in the flag evaluation failed, for instance
        /// due to a database error.
        /// </summary>
        StoreError
    }
}
