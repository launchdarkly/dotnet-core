using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Client.Interfaces;
using LaunchDarkly.Sdk.Client.Internal;
using LaunchDarkly.Sdk.Client.Subsystems;

using static LaunchDarkly.Sdk.Client.DataModel;
using static LaunchDarkly.Sdk.Client.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Client.Integrations
{
    /// <summary>
    /// A mechanism for providing dynamically updatable feature flag state in a simplified form to an SDK
    /// client in test scenarios.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This mechanism does not use any external resources. It provides only the data that the application
    /// has put into it using the <see cref="Update(TestData.FlagBuilder)"/> method.
    /// </para>
    /// <para>
    /// The example code below uses a simple boolean flag, but more complex configurations are possible using
    /// the methods of the <see cref="FlagBuilder"/> that is returned by <see cref="Flag(string)"/>.
    /// </para>
    /// <para>
    /// If the same <see cref="TestData"/> instance is used to configure multiple <see cref="LdClient"/>
    /// instances, any changes made to the data will propagate to all of the <see cref="LdClient"/>s.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    ///     var td = TestData.DataSource();
    ///     td.Update(td.Flag("flag-key-1").BooleanFlag().Variation(true));
    ///
    ///     var config = Configuration.Builder("sdk-key")
    ///         .DataSource(td)
    ///         .Build();
    ///     var client = new LdClient(config);
    ///
    ///     // flags can be updated at any time:
    ///     td.update(testData.flag("flag-key-2")
    ///         .VariationForUser("some-user-key", false));
    /// </code>
    /// </example>
    public sealed class TestData : IComponentConfigurer<IDataSource>
    {
        #region Private fields

        private readonly object _lock = new object();
        private readonly Dictionary<string, int> _currentFlagVersions =
            new Dictionary<string, int>();
        private readonly Dictionary<string, FlagBuilder> _currentBuilders =
            new Dictionary<string, FlagBuilder>();
        private readonly List<DataSourceImpl> _instances = new List<DataSourceImpl>();
        private TimeSpan _initializationDelay = TimeSpan.Zero;

        #endregion

        #region Private constructor

        private TestData() { }

        #endregion

        #region Public methods

        /// <summary>
        /// Creates a new instance of the test data source.
        /// </summary>
        /// <remarks>
        /// See <see cref="TestData"/> for details.
        /// </remarks>
        /// <returns>a new configurable test data source</returns>
        public static TestData DataSource() => new TestData();

        /// <summary>
        /// Sets an artificial delay before the data source initializes.
        /// </summary>
        /// <remarks>
        /// This is useful for testing scenarios where you need to observe intermediate
        /// states like <see cref="DataSourceState.Initializing"/> before the data source
        /// reaches <see cref="DataSourceState.Valid"/>.
        /// </remarks>
        /// <param name="delay">the delay before initialization</param>
        /// <returns>the same <see cref="TestData"/> instance</returns>
        public TestData WithInitializationDelay(TimeSpan delay)
        {
            _initializationDelay = delay;
            return this;
        }

        /// <summary>
        /// Creates or copies a <see cref="FlagBuilder"/> for building a test flag configuration.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If this flag key has already been defined in this <see cref="TestData"/> instance, then
        /// the builder starts with the same configuration that was last provided for this flag.
        /// </para>
        /// <para>
        /// Otherwise, it starts with a new default configuration in which the flag has <c>true</c>
        /// and <c>false</c> variations, and is <c>true</c> by default for all contexts. You can change
        /// any of those properties, and provide more complex behavior, using the
        /// <see cref="FlagBuilder"/> methods.
        /// </para>
        /// <para>
        /// Once you have set the desired configuration, pass the builder to
        /// <see cref="Update(FlagBuilder)"/>.
        /// </para>
        /// </remarks>
        /// <param name="key">the flag key</param>
        /// <returns>a flag configuration builder</returns>
        /// <seealso cref="Update(FlagBuilder)"/>
        public FlagBuilder Flag(string key)
        {
            FlagBuilder existingBuilder;
            lock (_lock)
            {
                _currentBuilders.TryGetValue(key, out existingBuilder);
            }
            if (existingBuilder != null)
            {
                return new FlagBuilder(existingBuilder);
            }
            return new FlagBuilder(key).BooleanFlag();
        }

        /// <summary>
        /// Updates the test data with the specified flag configuration.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This has the same effect as if a flag were added or modified on the LaunchDarkly dashboard.
        /// It immediately propagates the flag change to any <see cref="LdClient"/> instance(s) that
        /// you have already configured to use this <see cref="TestData"/>. If no <see cref="LdClient"/>
        /// has been started yet, it simply adds this flag to the test data which will be provided to any
        /// <see cref="LdClient"/> that you subsequently configure.
        /// </para>
        /// <para>
        /// Any subsequent changes to this <see cref="FlagBuilder"/> instance do not affect the test data,
        /// unless you call <see cref="Update(FlagBuilder)"/> again.
        /// </para>
        /// </remarks>
        /// <param name="flagBuilder">a flag configuration builder</param>
        /// <returns>the same <see cref="TestData"/> instance</returns>
        /// <seealso cref="Flag(string)"/>
        public TestData Update(FlagBuilder flagBuilder)
        {
            var key = flagBuilder._key;
            var clonedBuilder = new FlagBuilder(flagBuilder);
            UpdateInternal(key, clonedBuilder);
            return this;
        }

        private void UpdateInternal(string key, FlagBuilder builder)
        {
            DataSourceImpl[] instances;
            int newVersion;

            lock (_lock)
            {
                if (!_currentFlagVersions.TryGetValue(key, out var oldVersion))
                {
                    oldVersion = 0;
                }
                newVersion = oldVersion + 1;
                _currentFlagVersions[key] = newVersion;
                if (builder is null)
                {
                    _currentBuilders.Remove(key);
                }
                else
                {
                    _currentBuilders[key] = builder;
                }
                instances = _instances.ToArray();
            }

            foreach (var instance in instances)
            {
                instance.DoUpdate(key, builder.CreateFlag(newVersion, instance.Context));
            }
        }

        /// <summary>
        /// Simulates a change in the data source status.
        /// </summary>
        /// <remarks>
        /// Use this if you want to test the behavior of application code that uses
        /// <see cref="LdClient.DataSourceStatusProvider"/> to track whether the data source is having
        /// problems (for example, a network failure interrupting the streaming connection). It does
        /// not actually stop the <see cref="TestData"/> data source from working, so even if you have
        /// simulated an outage, calling <see cref="Update(FlagBuilder)"/> will still send updates.
        /// </remarks>
        /// <param name="newState">one of the constants defined by <see cref="DataSourceState"/></param>
        /// <param name="newError">an optional <see cref="DataSourceStatus.ErrorInfo"/> instance</param>
        /// <returns>the same <see cref="TestData"/> instance</returns>
        public TestData UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError)
        {
            DataSourceImpl[] instances;
            lock (_lock)
            {
                instances = _instances.ToArray();
            }
            foreach (var instance in instances)
            {
                instance.DoUpdateStatus(newState, newError);
            }
            return this;
        }

        /// <inheritdoc/>
        public IDataSource Build(LdClientContext clientContext)
        {
            var instance = new DataSourceImpl(
                this,
                clientContext.DataSourceUpdateSink,
                clientContext.CurrentContext,
                clientContext.BaseLogger.SubLogger("DataSource.TestData"),
                _initializationDelay
                );
            lock (_lock)
            {
                _instances.Add(instance);
            }
            return instance;
        }

        internal FullDataSet MakeInitData(Context context)
        {
            lock (_lock)
            {
                var b = ImmutableList.CreateBuilder<KeyValuePair<string, ItemDescriptor>>();
                foreach (var fb in _currentBuilders)
                {
                    if (!_currentFlagVersions.TryGetValue(fb.Key, out var version))
                    {
                        version = 1;
                        _currentFlagVersions[fb.Key] = version;
                    }
                    b.Add(new KeyValuePair<string, ItemDescriptor>(fb.Key,
                        fb.Value.CreateFlag(version, context)));
                }
                return new FullDataSet(b.ToImmutable());
            }
        }

        internal void ClosedInstance(DataSourceImpl instance)
        {
            lock (_lock)
            {
                _instances.Remove(instance);
            }
        }

        #endregion

        #region Public inner types

        /// <summary>
        /// A builder for feature flag configurations to be used with <see cref="TestData"/>.
        /// </summary>
        /// <seealso cref="TestData.Flag(string)"/>
        /// <seealso cref="TestData.Update(FlagBuilder)"/>
        public sealed class FlagBuilder
        {
            #region Private/internal fields

            private const int TrueVariationForBoolean = 0;
            private const int FalseVariationForBoolean = 1;

            internal readonly string _key;
            private List<LdValue> _variations;
            private int _defaultVariation;
            Dictionary<ContextKind, Dictionary<string, int>> _variationByContextKey =
                new Dictionary<ContextKind, Dictionary<string, int>>();
            private Func<Context, int?> _variationFunc;
            private FeatureFlag _preconfiguredFlag;

            #endregion

            #region Internal constructors

            internal FlagBuilder(string key)
            {
                _key = key;
                _variations = new List<LdValue>();
                _defaultVariation = 0;
            }

            internal FlagBuilder(FlagBuilder from)
            {
                _key = from._key;
                _variations = new List<LdValue>(from._variations);
                _defaultVariation = from._defaultVariation;
                _variationFunc = from._variationFunc;
                foreach (var kv in from._variationByContextKey)
                {
                    _variationByContextKey[kv.Key] = new Dictionary<string, int>(kv.Value);
                }
                _preconfiguredFlag = from._preconfiguredFlag;
            }

            #endregion

            #region Public methods

            /// <summary>
            /// A shortcut for setting the flag to use the standard boolean configuration.
            /// </summary>
            /// <remarks>
            /// This is the default for all new flags created with <see cref="TestData.Flag(string)"/>.
            /// The flag will have two variations, <c>true</c> and <c>false</c> (in that order). When
            /// using evaluation reasons, the reason will be set to <see cref="EvaluationReason.FallthroughReason"/>
            /// whenever the value is <c>true</c>, and <see cref="EvaluationReason.OffReason"/> whenever the
            /// value is <c>false</c>.
            /// </remarks>
            /// <returns>the builder</returns>
            public FlagBuilder BooleanFlag() =>
                IsBooleanFlag ? this : Variations(LdValue.Of(true), LdValue.Of(false));

            /// <summary>
            /// Sets the flag to return the specified boolean variation for all contexts by default.
            /// </summary>
            /// <remarks>
            /// The flag's variations are set to <c>true</c> and <c>false</c> if they are not already
            /// (equivalent to calling <see cref="BooleanFlag"/>).
            /// </remarks>
            /// <param name="variation">the desired true/false variation to be returned for all users</param>
            /// <returns>the builder</returns>
            public FlagBuilder Variation(bool variation) =>
                BooleanFlag().Variation(VariationForBoolean(variation));

            /// <summary>
            /// Sets the flag to return the specified variation for all contexts by default.
            /// </summary>
            /// <remarks>
            /// The variation is specified by number, out of whatever variation values have already been
            /// defined.
            /// </remarks>
            /// <param name="variationIndex">the desired variation: 0 for the first, 1 for the second, etc.</param>
            /// <returns>the builder</returns>
            public FlagBuilder Variation(int variationIndex)
            {
                _defaultVariation = variationIndex;
                return this;
            }

            /// <summary>
            /// Sets the flag to return the specified variation value for all contexts by default.
            /// </summary>
            /// <remarks>
            /// The value may be of any JSON type, as defined by <see cref="LdValue"/>. If the value
            /// matches one of the values previously specified with <see cref="Variations(LdValue[])"/>,
            /// then the variation index is set to the index of that value. Otherwise, the value is
            /// added to the variation list.
            /// </remarks>
            /// <param name="value">the desired value to be returned for all users</param>
            /// <returns>the builder</returns>
            public FlagBuilder Variation(LdValue value)
            {
                AddVariationIfNotDefined(value);
                _defaultVariation = _variations.IndexOf(value);
                _variationFunc = null;
                return this;
            }

            /// <summary>
            /// Sets the flag to return the specified boolean variation for a specific user key,
            /// overriding any other defaults.
            /// </summary>
            /// <remarks>
            /// The flag's variations are set to <c>true</c> and <c>false</c> if they are not already
            /// (equivalent to calling <see cref="BooleanFlag"/>).
            /// </remarks>
            /// <param name="userKey">the user key</param>
            /// <param name="variation">the desired true/false variation to be returned for this user</param>
            /// <returns>the builder</returns>
            /// <seealso cref="VariationForKey(ContextKind, string, bool)"/>
            /// <seealso cref="VariationForUser(string, int)"/>
            /// <seealso cref="VariationForUser(string, LdValue)"/>
            public FlagBuilder VariationForUser(string userKey, bool variation) =>
                VariationForKey(ContextKind.Default, userKey, variation);

            /// <summary>
            /// Sets the flag to return the specified variation for a specific user key, overriding
            /// any other defaults.
            /// </summary>
            /// <remarks>
            /// The variation is specified by number, out of whatever variation values have already been
            /// defined.
            /// </remarks>
            /// <param name="userKey">the user key</param>
            /// <param name="variationIndex">the desired variation to be returned for this user when
            /// targeting is on: 0 for the first, 1 for the second, etc.</param>
            /// <returns>the builder</returns>
            /// <seealso cref="VariationForKey(ContextKind, string, int)"/>
            /// <seealso cref="VariationForUser(string, bool)"/>
            /// <seealso cref="VariationForUser(string, LdValue)"/>
            public FlagBuilder VariationForUser(string userKey, int variationIndex) =>
                VariationForKey(ContextKind.Default, userKey, variationIndex);

            /// <summary>
            /// Sets the flag to return the specified variation value for a specific user key, overriding
            /// any other defaults.
            /// </summary>
            /// <remarks>
            /// The value may be of any JSON type, as defined by <see cref="LdValue"/>. If the value
            /// matches one of the values previously specified with <see cref="Variations(LdValue[])"/>,
            /// then the variation index is set to the index of that value. Otherwise, the value is
            /// added to the variation list.
            /// </remarks>
            /// <param name="userKey">a user key</param>
            /// <param name="value">the desired value to be returned for this user</param>
            /// <returns>the builder</returns>
            /// <seealso cref="VariationForKey(ContextKind, string, LdValue)"/>
            /// <seealso cref="VariationForUser(string, bool)"/>
            /// <seealso cref="VariationForUser(string, int)"/>
            public FlagBuilder VariationForUser(string userKey, LdValue value) =>
                VariationForKey(ContextKind.Default, userKey, value);

            /// <summary>
            /// Sets the flag to return the specified boolean variation for a specific context by kind
            /// and key, overriding any other defaults.
            /// </summary>
            /// <remarks>
            /// The flag's variations are set to <c>true</c> and <c>false</c> if they are not already
            /// (equivalent to calling <see cref="BooleanFlag"/>).
            /// </remarks>
            /// <param name="contextKind">the context kind</param>
            /// <param name="contextKey">the context key</param>
            /// <param name="variation">the desired true/false variation to be returned for this context</param>
            /// <returns>the builder</returns>
            /// <seealso cref="VariationForUser(string, bool)"/>
            /// <seealso cref="VariationForKey(ContextKind, string, int)"/>
            /// <seealso cref="VariationForKey(ContextKind, string, LdValue)"/>
            public FlagBuilder VariationForKey(ContextKind contextKind, string contextKey, bool variation) =>
                BooleanFlag().VariationForKey(contextKind, contextKey, VariationForBoolean(variation));

            /// <summary>
            /// Sets the flag to return the specified variation for a specific context by kind and key,
            /// overriding any other defaults.
            /// </summary>
            /// <remarks>
            /// The variation is specified by number, out of whatever variation values have already been
            /// defined.
            /// </remarks>
            /// <param name="contextKind">the context kind</param>
            /// <param name="contextKey">the context key</param>
            /// <param name="variationIndex">the desired variation to be returned for this context when
            /// targeting is on: 0 for the first, 1 for the second, etc.</param>
            /// <returns>the builder</returns>
            /// <seealso cref="VariationForUser(string, int)"/>
            /// <seealso cref="VariationForKey(ContextKind, string, bool)"/>
            /// <seealso cref="VariationForKey(ContextKind, string, LdValue)"/>
            public FlagBuilder VariationForKey(ContextKind contextKind, string contextKey, int variationIndex)
            {
                if (!_variationByContextKey.TryGetValue(contextKind, out var keys))
                {
                    keys = new Dictionary<string, int>();
                    _variationByContextKey[contextKind] = keys;
                }
                keys[contextKey] = variationIndex;
                return this;
            }

            /// <summary>
            /// Sets the flag to return the specified variation value for a specific context by kind and
            /// key, overriding any other defaults.
            /// </summary>
            /// <remarks>
            /// The value may be of any JSON type, as defined by <see cref="LdValue"/>. If the value
            /// matches one of the values previously specified with <see cref="Variations(LdValue[])"/>,
            /// then the variation index is set to the index of that value. Otherwise, the value is
            /// added to the variation list.
            /// </remarks>
            /// <param name="contextKind">the context kind</param>
            /// <param name="contextKey">the context key</param>
            /// <param name="value">the desired value to be returned for this context</param>
            /// <returns>the builder</returns>
            /// <seealso cref="VariationForUser(string, LdValue)"/>
            /// <seealso cref="VariationForKey(ContextKind, string, bool)"/>
            /// <seealso cref="VariationForKey(ContextKind, string, int)"/>
            public FlagBuilder VariationForKey(ContextKind contextKind, string contextKey, LdValue value) =>
                VariationForKey(contextKind, contextKey, AddVariationIfNotDefined(value));

            /// <summary>
            /// Sets the flag to use a function to determine whether to return true or false for
            /// any given context.
            /// </summary>
            /// <remarks>
            /// <para>
            /// The function takes an evaluation context and returns <see langword="true"/>, <see langword="false"/>,
            /// or <see langword="null"/>. A <see langword="null"/> result means that the flag will
            /// fall back to its default variation for all contexts.
            /// </para>
            /// <para>
            /// The flag's variations are set to <c>true</c> and <c>false</c> if they are not already
            /// (equivalent to calling <see cref="BooleanFlag"/>).
            /// </para>
            /// <para>
            /// This function is called only if the context was not specifically targeted with
            /// <see cref="VariationForUser(string, bool)"/> or <see cref="VariationForKey(ContextKind, string, bool)"/>.
            /// </para>
            /// </remarks>
            /// <param name="variationFunc">a function to determine the variation</param>
            /// <returns>the builder</returns>
            public FlagBuilder VariationFunc(Func<Context, bool?> variationFunc) =>
                BooleanFlag().VariationFunc(context =>
                    {
                        var b = variationFunc(context);
                        return b.HasValue ? VariationForBoolean(b.Value) : (int?)null;
                    });

            /// <summary>
            /// Sets the flag to use a function to determine the variation index to return for
            /// any given context.
            /// </summary>
            /// <remarks>
            /// <para>
            /// The function takes an evaluation context and returns an integer variation index or <see langword="null"/>.
            /// A <see langword="null"/> result means that the flag will fall back to its default
            /// variation for all contexts.
            /// </para>
            /// <para>
            /// This function is called only if the context was not specifically targeted with
            /// <see cref="VariationForUser(string, int)"/> or <see cref="VariationForKey(ContextKind, string, int)"/>.
            /// </para>
            /// </remarks>
            /// <param name="variationFunc">a function to determine the variation</param>
            /// <returns>the builder</returns>
            public FlagBuilder VariationFunc(Func<Context, int?> variationFunc)
            {
                _variationFunc = variationFunc;
                return this;
            }

            /// <summary>
            /// Sets the flag to use a function to determine the variation value to return for
            /// any given context.
            /// </summary>
            /// <remarks>
            /// <para>
            /// The function takes an evaluation context and returns an <see cref="LdValue"/> or <see langword="null"/>.
            /// A <see langword="null"/> result means that the flag will fall back to its default
            /// variation for all contexts.
            /// </para>
            /// <para>
            /// The value returned by the function must be one of the values previously specified
            /// with <see cref="Variations(LdValue[])"/>; otherwise it will be ignored.
            /// </para>
            /// <para>
            /// This function is called only if the context was not specifically targeted with
            /// <see cref="VariationForUser(string, LdValue)"/> or <see cref="VariationForKey(ContextKind, string, LdValue)"/>.
            /// </para>
            /// </remarks>
            /// <param name="variationFunc">a function to determine the variation</param>
            /// <returns>the builder</returns>
            public FlagBuilder VariationFunc(Func<Context, LdValue?> variationFunc) =>
                VariationFunc(context =>
                    {
                        var v = variationFunc(context);
                        if (!v.HasValue || !_variations.Contains(v.Value))
                        {
                            return null;
                        }
                        return _variations.IndexOf(v.Value);
                    });

            /// <summary>
            /// Changes the allowable variation values for the flag.
            /// </summary>
            /// <remarks>
            /// The value may be of any JSON type, as defined by <see cref="LdValue"/>. For instance, a
            /// boolean flag normally has <c>LdValue.Of(true), LdValue.Of(false)</c>; a string-valued
            /// flag might have <c>LdValue.Of("red"), LdValue.Of("green"), LdValue.Of("blue")</c>; etc.
            /// </remarks>
            /// <param name="values">the desired variations</param>
            /// <returns>the builder</returns>
            public FlagBuilder Variations(params LdValue[] values)
            {
                _variations.Clear();
                _variations.AddRange(values);
                return this;
            }

            // For testing only
            internal FlagBuilder PreconfiguredFlag(FeatureFlag preconfiguredFlag)
            {
                _preconfiguredFlag = preconfiguredFlag;
                return this;
            }

            #endregion

            #region Internal methods

            internal ItemDescriptor CreateFlag(int version, Context context)
            {
                if (_preconfiguredFlag != null)
                {
                    return new ItemDescriptor(version, new FeatureFlag(
                        _preconfiguredFlag.Value,
                        _preconfiguredFlag.Variation,
                        _preconfiguredFlag.Reason,
                        _preconfiguredFlag.Version > version ? _preconfiguredFlag.Version : version,
                        _preconfiguredFlag.FlagVersion,
                        _preconfiguredFlag.TrackEvents,
                        _preconfiguredFlag.TrackReason,
                        _preconfiguredFlag.DebugEventsUntilDate,
                        _preconfiguredFlag.Prerequisites));
                }
                int variation;
                if (!_variationByContextKey.TryGetValue(context.Kind, out var keys) ||
                    !keys.TryGetValue(context.Key, out variation))
                {
                    variation = _variationFunc?.Invoke(context) ?? _defaultVariation;
                }
                var value = (variation < 0 || variation >= _variations.Count) ? LdValue.Null :
                    _variations[variation];
                var reason = variation == 0 ? EvaluationReason.FallthroughReason :
                    EvaluationReason.OffReason;
                var flag = new FeatureFlag(
                    value,
                    variation,
                    reason,
                    version,
                    null,
                    false,
                    false,
                    null
                    );
                return new ItemDescriptor(version, flag);
            }

            internal bool IsBooleanFlag =>
                _variations.Count == 2 &&
                    _variations[TrueVariationForBoolean] == LdValue.Of(true) &&
                    _variations[FalseVariationForBoolean] == LdValue.Of(false);

            internal int AddVariationIfNotDefined(LdValue value)
            {
                int i = _variations.IndexOf(value);
                if (i >= 0)
                {
                    return i;
                }
                _variations.Add(value);
                return _variations.Count - 1;
            }

            internal static int VariationForBoolean(bool value) =>
                value ? TrueVariationForBoolean : FalseVariationForBoolean;

            #endregion
        }

        #endregion

        #region Internal inner type

        internal class DataSourceImpl : IDataSource
        {
            private readonly TestData _parent;
            private readonly IDataSourceUpdateSink _updateSink;
            private readonly Logger _log;
            private readonly TimeSpan _initializationDelay;

            internal readonly Context Context;

            internal DataSourceImpl(TestData parent, IDataSourceUpdateSink updateSink, Context context, Logger log, TimeSpan initializationDelay)
            {
                _parent = parent;
                _updateSink = updateSink;
                Context = context;
                _log = log;
                _initializationDelay = initializationDelay;
            }

            public async Task<bool> Start()
            {
                if (_initializationDelay > TimeSpan.Zero)
                {
                    await Task.Delay(_initializationDelay);
                }
                _updateSink.Init(Context, _parent.MakeInitData(Context));
                return true;
            }

            public bool Initialized => true;

            public void Dispose() =>
                _parent.ClosedInstance(this);

            internal void DoUpdate(string key, ItemDescriptor item)
            {
                _log.Debug("updating \"{0}\" to {1}", key, LogValues.Defer(() =>
                    item.Item is null ? "<null>" : DataModelSerialization.SerializeFlag(item.Item)));
                _updateSink.Upsert(Context, key, item);
            }

            internal void DoUpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError)
            {
                _log.Debug("updating status to {0}{1}", newState,
                    newError.HasValue ? (" (" + newError.Value + ")") : "");
                _updateSink.UpdateStatus(newState, newError);
            }
        }

        #endregion
    }
}
