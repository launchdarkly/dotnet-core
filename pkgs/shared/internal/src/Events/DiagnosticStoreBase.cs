using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using LaunchDarkly.Sdk.Internal.Http;

namespace LaunchDarkly.Sdk.Internal.Events
{
    /// <summary>
    /// Abstract implementation of IDiagnosticStore.
    /// </summary>
    /// <remarks>
    /// Platform-specific behavior is provided by subclass overrides.
    /// </remarks>
    public abstract class DiagnosticStoreBase : IDiagnosticStore
    {
        private readonly DateTime _initTime;
        private DiagnosticId _diagnosticId;

        // _dataSince is stored in the "binary" long format so Interlocked.Exchange can be used
        private long _dataSince;
        private long _droppedEvents;
        private long _deduplicatedUsers;
        private long _eventsInLastBatch;
        private readonly object _streamInitsLock = new object();
        private LdValue.ArrayBuilder _streamInits = LdValue.BuildArray();

        #region IDiagnosticStore properties

        /// <inheritdoc/>
        public DiagnosticEvent? InitEvent => MakeInitEvent();

        /// <inheritdoc/>
        public DiagnosticEvent? PersistedUnsentEvent => null;

        /// <inheritdoc/>
        public DateTime DataSince => DateTime.FromBinary(Interlocked.Read(ref _dataSince));

        #endregion

        #region Abstract properties for SDKs to override

        /// <summary>
        /// Subclasses override this property to return the configured SDK key or mobile key.
        /// </summary>
        protected abstract string SdkKeyOrMobileKey { get; }

        /// <summary>
        /// Subclasses override this property to return a string such as "dotnet-server-sdk".
        /// </summary>
        protected abstract string SdkName { get; }

        /// <summary>
        /// Subclasses override this property to return one or more JSON objects which will
        /// be merged together to form the configuration properties. For convenience, these
        /// are represented with the <see cref="LdValue"/> type, but any values that are not
        /// JSON objects will be ignored.
        /// </summary>
        protected abstract IEnumerable<LdValue> ConfigProperties { get; }

        /// <summary>
        /// Subclasses override this property to return a string such as "netstandard2.0".
        /// </summary>
        protected abstract string DotNetTargetFramework { get; }

        /// <summary>
        /// Subclasses override this property to return the configured HTTP properties.
        /// </summary>
        protected abstract HttpProperties HttpProperties { get; }

        /// <summary>
        /// Subclasses override this property to return the type of the SDK's client class,
        /// which is used to determine the version via reflection.
        /// </summary>
        protected abstract Type TypeOfLdClient { get; }

        #endregion

        #region Constructor

        /// <summary>
        /// Base class constructor.
        /// </summary>
        protected DiagnosticStoreBase()
        {
            _initTime = DateTime.Now;
            _dataSince = _initTime.ToBinary();
        }

        #endregion

        #region Periodic event update and builder methods

        /// <inheritdoc/>
        public void IncrementDeduplicatedUsers() =>
            Interlocked.Increment(ref _deduplicatedUsers);

        /// <inheritdoc/>
        public void IncrementDroppedEvents() =>
            Interlocked.Increment(ref _droppedEvents);

        /// <inheritdoc/>
        public void AddStreamInit(DateTime timestamp, TimeSpan duration, bool failed)
        {
            var streamInitObject = LdValue.BuildObject();
            streamInitObject.Add("timestamp", UnixMillisecondTime.FromDateTime(timestamp).Value);
            streamInitObject.Add("durationMillis", duration.TotalMilliseconds);
            streamInitObject.Add("failed", failed);
            lock (_streamInitsLock)
            {
                _streamInits.Add(streamInitObject.Build());
            }
        }

        /// <inheritdoc/>
        public void RecordEventsInBatch(long eventsInBatch) =>
            Interlocked.Exchange(ref _eventsInLastBatch, eventsInBatch);

        /// <inheritdoc/>
        public DiagnosticEvent CreateEventAndReset()
        {
            DateTime currentTime = DateTime.Now;
            long droppedEvents = Interlocked.Exchange(ref _droppedEvents, 0);
            long deduplicatedUsers = Interlocked.Exchange(ref _deduplicatedUsers, 0);
            long eventsInLastBatch = Interlocked.Exchange(ref _eventsInLastBatch, 0);
            long dataSince = Interlocked.Exchange(ref _dataSince, currentTime.ToBinary());

            var statEvent = LdValue.BuildObject();
            AddDiagnosticCommonFields(statEvent, "diagnostic", currentTime);
            statEvent.Add("eventsInLastBatch", eventsInLastBatch);
            statEvent.Add("dataSinceDate", UnixMillisecondTime.FromDateTime(DateTime.FromBinary(dataSince)).Value);
            statEvent.Add("droppedEvents", droppedEvents);
            statEvent.Add("deduplicatedUsers", deduplicatedUsers);
            lock (_streamInitsLock)
            {
                statEvent.Add("streamInits", _streamInits.Build());
                _streamInits = LdValue.BuildArray();
            }

            return new DiagnosticEvent(statEvent.Build());
        }

        #endregion

        #region Private methods for building event data

        private DiagnosticId GetDiagnosticId()
        {
            if (_diagnosticId is null)
            {
                _diagnosticId = new DiagnosticId(SdkKeyOrMobileKey, Guid.NewGuid());
            }
            return _diagnosticId;
        }

        private void AddDiagnosticCommonFields(LdValue.ObjectBuilder fieldsBuilder, string kind, DateTime creationDate)
        {
            fieldsBuilder.Add("kind", kind);
            fieldsBuilder.Add("id", EncodeDiagnosticId(GetDiagnosticId()));
            fieldsBuilder.Add("creationDate", UnixMillisecondTime.FromDateTime(creationDate).Value);
        }

        private LdValue EncodeDiagnosticId(DiagnosticId id)
        {
            var o = LdValue.BuildObject().Add("diagnosticId", id.Id.ToString());
            if (id.SdkKeySuffix != null)
            {
                o.Add("sdkKeySuffix", id.SdkKeySuffix);
            }
            return o.Build();
        }

        private DiagnosticEvent MakeInitEvent()
        {
            var initEvent = LdValue.BuildObject();

            var configBuilder = LdValue.BuildObject();
            foreach (var configProps in ConfigProperties)
            {
                if (configProps.Type == LdValueType.Object)
                {
                    foreach (var prop in configProps.AsDictionary(LdValue.Convert.Json))
                    {
                        configBuilder.Add(prop.Key, prop.Value);
                    }
                }
            }
            initEvent.Add("configuration", configBuilder.Build());

            initEvent.Add("sdk", InitEventSdk());
            initEvent.Add("platform", InitEventPlatform());
            AddDiagnosticCommonFields(initEvent, "diagnostic-init", _initTime);
            return new DiagnosticEvent(initEvent.Build());
        }

        private LdValue InitEventPlatform() =>
            LdValue.BuildObject()
                .Add("name", "dotnet")
                .Add("dotNetTargetFramework", LdValue.Of(DotNetTargetFramework))
                .Add("osName", LdValue.Of(GetOSName()))
                .Add("osVersion", LdValue.Of(GetOSVersion()))
                .Add("osArch", LdValue.Of(GetOSArch()))
                .Build();

        private LdValue InitEventSdk()
        {
            var sdkInfo = LdValue.BuildObject()
                .Add("name", SdkName)
                .Add("version", AssemblyVersions.GetAssemblyVersionStringForType(TypeOfLdClient));
            foreach (var kv in HttpProperties.BaseHeaders)
            {
                if (kv.Key.ToLower() == "x-launchdarkly-wrapper")
                {
                    if (kv.Value.Contains("/"))
                    {
                        sdkInfo.Add("wrapperName", kv.Value.Substring(0, kv.Value.IndexOf("/")));
                        sdkInfo.Add("wrapperVersion", kv.Value.Substring(kv.Value.IndexOf("/") + 1));
                    }
                    else
                    {
                        sdkInfo.Add("wrapperName", kv.Value);
                    }
                }
            }
            return sdkInfo.Build();
        }

        internal static string GetOSName()
        {
            // Environment.OSVersion.Platform is another way to get this information, except that it does not
            // reliably distinguish between MacOS and Linux.

#if NETFRAMEWORK
            // .NET Framework 4.6 does not support RuntimeInformation.ISOSPlatform. We could use Environment.OSVersion.Platform
            // instead (it's similar, except that it can't reliably distinguish between MacOS and Linux)... but .NET 4.5 can't
            // run on anything but Windows anyway.
            return "Windows";
#else
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return "Linux";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "MacOS";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "Windows";
            }
            return "unknown";
#endif
        }

        internal static string GetOSVersion()
        {
            // .NET's way of reporting Windows versions is very idiosyncratic, e.g. Windows 8 is "6.2", but we'll
            // just report what it says and translate it later when we look at the analytics.
            return Environment.OSVersion.Version.ToString();
        }

        internal static string GetOSArch()
        {
#if NETFRAMEWORK
            // .NET Framework 4.6 does not support RuntimeInformation.OSArchitecture
            return "unknown";
#else
            return RuntimeInformation.OSArchitecture.ToString().ToLower(); // "arm", "arm64", "x64", "x86"
#endif
        }

        #endregion
    }
}
