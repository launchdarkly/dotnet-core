namespace LaunchDarkly.Sdk.Server.Hooks
{
    /// <summary>
    /// EvaluationSeriesContext represents parameters associated with a feature flag evaluation. It is
    /// made available in <see cref="Hook"/> stage callbacks.
    /// </summary>
    public sealed class EvaluationSeriesContext {
        /// <summary>
        /// The key of the feature flag.
        /// </summary>
        public string FlagKey { get;  }

        /// <summary>
        /// The Context used for evaluation.
        /// </summary>
        public Context Context { get;  }

        /// <summary>
        /// The user-provided default value for the evaluation.
        /// </summary>
        public LdValue DefaultValue { get;  }

        /// <summary>
        /// The variation method that triggered the evaluation.
        /// </summary>
        public string Method { get;  }

        /// <summary>
        /// The environment ID for the evaluation, or null if not available.
        /// </summary>
        public string EnvironmentId { get; }

        /// <summary>
        /// Constructs a new EvaluationSeriesContext.
        /// </summary>
        /// <param name="flagKey">the flag key</param>
        /// <param name="context">the context</param>
        /// <param name="defaultValue">the default value</param>
        /// <param name="method">the variation method</param>
        /// <param name="environmentId">the environment ID</param>
        public EvaluationSeriesContext(string flagKey, Context context, LdValue defaultValue, string method,
            string environmentId = null) {
            FlagKey = flagKey;
            Context = context;
            DefaultValue = defaultValue;
            Method = method;
            EnvironmentId = environmentId;
        }
    }
}
