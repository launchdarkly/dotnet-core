using System;
using System.Collections.Generic;
using Consul;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    /// <summary>
    /// A builder for configuring the Consul-based persistent data store.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Obtain an instance of this class by calling <see cref="Consul.DataStore"/>. After calling its methods
    /// to specify any desired custom settings, wrap it in a <see cref="PersistentDataStoreBuilder"/>
    /// by calling <see cref="Components.PersistentDataStore(IComponentConfigurer{IPersistentDataStoreAsync})"/>, then pass
    /// the result into the SDK configuration with <see cref="ConfigurationBuilder.DataStore(IComponentConfigurer{IDataStore})"/>.
    /// You do not need to call <see cref="Build(LdClientContext)"/> yourself to build
    /// the actual data store; that will be done by the SDK.
    /// </para>
    /// <para>
    /// The Consul client has many configuration options. This class has shortcut methods for
    /// some of them, but if you need more sophisticated control over the Consul client, use
    /// <see cref="ConsulConfigChanges(Action{ConsulClientConfiguration})"/> or
    /// <see cref="ExistingClient(ConsulClient)"/>.
    /// </para>
    /// <para>
    /// Builder calls can be chained, for example:
    /// </para>
    /// <code>
    ///     var config = Configuration.Builder("sdk-key")
    ///         .DataStore(
    ///             Components.PersistentDataStore(
    ///                 Consul.DataStore()
    ///                     .Address("http://my-consul-host:8500")
    ///                     .Prefix("app1")
    ///                 )
    ///                 .CacheSeconds(15)
    ///             )
    ///         .Build();
    /// </code>
    /// </remarks>
    public sealed class ConsulDataStoreBuilder : IComponentConfigurer<IPersistentDataStoreAsync>,
        IDiagnosticDescription
    {
        private ConsulClient _existingClient;
        private List<Action<ConsulClientConfiguration>> _configActions = new List<Action<ConsulClientConfiguration>>();
        private Uri _address;
        private string _prefix = Consul.DefaultPrefix;
        
        internal ConsulDataStoreBuilder() { }

        /// <summary>
        /// Shortcut for calling <see cref="Address(Uri)"/> with a string.
        /// </summary>
        /// <param name="address">the URI of the Consul host as a string</param>
        /// <returns>the builder</returns>
        /// <seealso cref="Address(Uri)"/>
        public ConsulDataStoreBuilder Address(string address) => Address(new Uri(address));

        /// <summary>
        /// Specifies the Consul agent's location.
        /// </summary>
        /// <param name="address">the URI of the Consul host</param>
        /// <returns>the builder</returns>
        /// /// <seealso cref="Address(string)"/>
        public ConsulDataStoreBuilder Address(Uri address)
        {
            _address = address;
            return this;
        }

        /// <summary>
        /// Specifies custom steps for configuring the Consul client. Your action may modify the
        /// <see cref="ConsulClientConfiguration"/> object in any way.
        /// </summary>
        /// <param name="configAction">an action for modifying the configuration</param>
        /// <returns>the builder</returns>
        public ConsulDataStoreBuilder ConsulConfigChanges(Action<ConsulClientConfiguration> configAction)
        {
            if (configAction != null)
            {
                _configActions.Add(configAction);
            }
            return this;
        }

        /// <summary>
        /// Specifies an existing, already-configured Consul client instance that the data store
        /// should use rather than creating one of its own.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If you specify an existing client, then the other builder methods for configuring Consul are ignored.
        /// </para>
        /// <para>
        /// Note that the LaunchDarkly code will <i>not</i> take ownership of the lifecycle of this
        /// object: in other words, it will not call <c>Dispose()</c> on the <c>ConsulClient</c> when
        /// you dispose of the SDK client, as it would if it had created the <c>ConsulClient</c> itself.
        /// It is your responsibility to call <c>Dispose()</c> on the <c>ConsulClient</c> when you are
        /// done with it.
        /// </para>
        /// </remarks>
        /// <param name="client">an existing Consul client instance</param>
        /// <returns>the builder</returns>
        public ConsulDataStoreBuilder ExistingClient(ConsulClient client)
        {
            _existingClient = client;
            return this;
        }

        /// <summary>
        /// Sets an optional namespace prefix for all keys stored in Consul.
        /// </summary>
        /// <remarks>
        /// Use this if you are sharing the same database table between multiple clients that are for
        /// different LaunchDarkly environments, to avoid key collisions.
        /// </remarks>
        /// <param name="prefix">the namespace prefix, or null to use <see cref="Consul.DefaultPrefix"/></param>
        /// <returns>the builder</returns>
        public ConsulDataStoreBuilder Prefix(string prefix)
        {
            _prefix = string.IsNullOrEmpty(prefix) ? Consul.DefaultPrefix : prefix;
            return this;
        }

        /// <inheritdoc/>
        public IPersistentDataStoreAsync Build(LdClientContext context)
        {
            var client = _existingClient;
            if (client is null)
            {
                client = new ConsulClient(config =>
                {
                    if (_address != null)
                    {
                        config.Address = _address;
                    }
                    foreach (var action in _configActions)
                    {
                        action.Invoke(config);
                    }
                });
            }

            return new ConsulDataStoreImpl(
                client,
                _existingClient != null,
                _prefix,
                context.Logger.SubLogger("DataStore.Consul")
                );
        }

        /// <inheritdoc/>
        public LdValue DescribeConfiguration(LdClientContext context) =>
            LdValue.Of("Consul");
    }
}
