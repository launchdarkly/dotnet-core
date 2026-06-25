using System;
using System.Collections.Generic;
using LaunchDarkly.Sdk.Internal.Http;
using Xunit;

namespace LaunchDarkly.Sdk.Internal.Events
{
    public class DiagnosticStoreBaseTest
    {
        const string FakeKey = "secret-example";
        const string FakeKeySuffix = "xample";
        const string FakeSdkName = "my-sdk-name";
        const string FakeTargetFramework = "example-framework";

        class DiagnosticStoreImpl : DiagnosticStoreBase
        {
            public IEnumerable<LdValue> _configProperties = new List<LdValue>();
            public HttpProperties _httpProperties = HttpProperties.Default;

            protected override string SdkKeyOrMobileKey => FakeKey;
            protected override string SdkName => FakeSdkName;
            protected override IEnumerable<LdValue> ConfigProperties => _configProperties;
            protected override string DotNetTargetFramework => FakeTargetFramework;
            protected override HttpProperties HttpProperties => _httpProperties;
            protected override Type TypeOfLdClient => typeof(DiagnosticStoreBaseTest);
        }

        [Fact]
        public void PersistedEventIsNullInitially()
        {
            var store = new DiagnosticStoreImpl();
            var persistedEvent = store.PersistedUnsentEvent;
            Assert.Null(persistedEvent);
        }

        [Fact]
        public void PeriodicEventDefaultValuesAreCorrect()
        {
            var store = new DiagnosticStoreImpl();
            DateTime dataSince = store.DataSince;
            LdValue periodicEvent = store.CreateEventAndReset().JsonValue;

            Assert.Equal("diagnostic", periodicEvent.Get("kind").AsString);
            Assert.Equal(UnixMillisecondTime.FromDateTime(dataSince).Value, periodicEvent.Get("dataSinceDate").AsLong);
            Assert.Equal(0, periodicEvent.Get("eventsInLastBatch").AsInt);
            Assert.Equal(0, periodicEvent.Get("droppedEvents").AsInt);
            Assert.Equal(0, periodicEvent.Get("deduplicatedUsers").AsInt);

            LdValue streamInits = periodicEvent.Get("streamInits");
            Assert.Equal(0, streamInits.Count);
        }

        [Fact]
        public void PeriodicEventUsesIdFromInit()
        {
            var store = new DiagnosticStoreImpl();
            DiagnosticEvent? initEvent = store.InitEvent;
            Assert.True(initEvent.HasValue);
            DiagnosticEvent periodicEvent = store.CreateEventAndReset();
            Assert.Equal(initEvent.Value.JsonValue.Get("id"), periodicEvent.JsonValue.Get("id"));
        }

        [Fact]
        public void CanIncrementDeduplicateUsers()
        {
            var store = new DiagnosticStoreImpl();
            store.IncrementDeduplicatedUsers();
            DiagnosticEvent periodicEvent = store.CreateEventAndReset();
            Assert.Equal(1, periodicEvent.JsonValue.Get("deduplicatedUsers").AsInt);
        }

        [Fact]
        public void CanIncrementDroppedEvents()
        {
            var store = new DiagnosticStoreImpl();
            store.IncrementDroppedEvents();
            DiagnosticEvent periodicEvent = store.CreateEventAndReset();
            Assert.Equal(1, periodicEvent.JsonValue.Get("droppedEvents").AsInt);
        }

        [Fact]
        public void CanRecordEventsInBatch()
        {
            var store = new DiagnosticStoreImpl();
            store.RecordEventsInBatch(4);
            DiagnosticEvent periodicEvent = store.CreateEventAndReset();
            Assert.Equal(4, periodicEvent.JsonValue.Get("eventsInLastBatch").AsInt);
        }

        [Fact]
        public void CanAddStreamInit()
        {
            var store = new DiagnosticStoreImpl();
            DateTime timestamp = DateTime.Now;
            store.AddStreamInit(timestamp, TimeSpan.FromMilliseconds(200.0), true);
            DiagnosticEvent periodicEvent = store.CreateEventAndReset();

            LdValue streamInits = periodicEvent.JsonValue.Get("streamInits");
            Assert.Equal(1, streamInits.Count);

            LdValue streamInit = streamInits.Get(0);
            Assert.Equal(UnixMillisecondTime.FromDateTime(timestamp).Value, streamInit.Get("timestamp").AsLong);
            Assert.Equal(200, streamInit.Get("durationMillis").AsInt);
            Assert.True(streamInit.Get("failed").AsBool);
        }

        [Fact]
        public void DataSinceFromLastDiagnostic()
        {
            var store = new DiagnosticStoreImpl();
            DiagnosticEvent periodicEvent = store.CreateEventAndReset();
            Assert.Equal(periodicEvent.JsonValue.Get("creationDate").AsLong,
                UnixMillisecondTime.FromDateTime(store.DataSince).Value);
        }

        [Fact]
        public void CreatingEventResetsFields()
        {
            var store = new DiagnosticStoreImpl();
            store.IncrementDroppedEvents();
            store.IncrementDeduplicatedUsers();
            store.RecordEventsInBatch(10);
            store.AddStreamInit(DateTime.Now, TimeSpan.FromMilliseconds(200.0), true);
            LdValue firstPeriodicEvent = store.CreateEventAndReset().JsonValue;
            LdValue nextPeriodicEvent = store.CreateEventAndReset().JsonValue;

            Assert.Equal(firstPeriodicEvent.Get("creationDate"), nextPeriodicEvent.Get("dataSinceDate"));
            Assert.Equal(0, nextPeriodicEvent.Get("eventsInLastBatch").AsInt);
            Assert.Equal(0, nextPeriodicEvent.Get("droppedEvents").AsInt);
            Assert.Equal(0, nextPeriodicEvent.Get("deduplicatedUsers").AsInt);
            Assert.Equal(0, nextPeriodicEvent.Get("eventsInLastBatch").AsInt);
            LdValue streamInits = nextPeriodicEvent.Get("streamInits");
            Assert.Equal(0, streamInits.Count);
        }

        [Fact]
        public void InitEventBaseProperties()
        {
            var store = new DiagnosticStoreImpl();
            var e = store.InitEvent.Value.JsonValue;
            Assert.Equal(LdValue.Of("diagnostic-init"), e.Get("kind"));
            Assert.Equal(LdValueType.Number, e.Get("creationDate").Type);

            var idProps = e.Get("id");
            Assert.NotEqual(LdValue.Null, idProps);
            Assert.Equal(LdValue.Of(FakeKeySuffix), idProps.Get("sdkKeySuffix"));
            Assert.NotEqual(LdValue.Null, idProps.Get("diagnosticId"));
        }

        [Fact]
        public void InitEventSdkProperties()
        {
            var store = new DiagnosticStoreImpl();
            var e = store.InitEvent.Value.JsonValue;
            var sdkProps = e.Get("sdk");
            Assert.NotEqual(LdValue.Null, sdkProps);
            Assert.Equal(LdValue.Of(FakeSdkName), sdkProps.Get("name"));
            Assert.Equal(LdValueType.String, sdkProps.Get("version").Type);
            Assert.Equal(LdValue.Null, sdkProps.Get("wrapperName"));
            Assert.Equal(LdValue.Null, sdkProps.Get("wrapperVersion"));
        }

        [Fact]
        public void InitEventSdkPropertiesWithWrapperName()
        {
            var store = new DiagnosticStoreImpl();
            store._httpProperties = store._httpProperties.WithWrapper("my-wrapper-name", null);

            var e = store.InitEvent.Value.JsonValue;
            var sdkProps = e.Get("sdk");
            Assert.NotEqual(LdValue.Null, sdkProps);
            Assert.Equal(LdValue.Of("my-wrapper-name"), sdkProps.Get("wrapperName"));
            Assert.Equal(LdValue.Null, sdkProps.Get("wrapperVersion"));
        }

        [Fact]
        public void InitEventSdkPropertiesWithWrapperNameAndVersion()
        {
            var store = new DiagnosticStoreImpl();
            store._httpProperties = store._httpProperties.WithWrapper("my-wrapper-name", "my-version");

            var e = store.InitEvent.Value.JsonValue;
            var sdkProps = e.Get("sdk");
            Assert.NotEqual(LdValue.Null, sdkProps);
            Assert.Equal(LdValue.Of("my-wrapper-name"), sdkProps.Get("wrapperName"));
            Assert.Equal(LdValue.Of("my-version"), sdkProps.Get("wrapperVersion"));
        }

        [Fact]
        public void InitEventPlatformProperties()
        {
            var store = new DiagnosticStoreImpl();
            var e = store.InitEvent.Value.JsonValue;
            var platformProps = e.Get("platform");
            Assert.NotEqual(LdValue.Null, platformProps);
            Assert.Equal(LdValue.Of("dotnet"), platformProps.Get("name"));
            Assert.Equal(LdValue.Of(FakeTargetFramework), platformProps.Get("dotNetTargetFramework"));
            Assert.Equal(LdValueType.String, platformProps.Get("osName").Type);
            Assert.Equal(LdValueType.String, platformProps.Get("osVersion").Type);
            Assert.Equal(LdValueType.String, platformProps.Get("osArch").Type);
        }

        [Fact]
        public void InitEventConfigPropertiesWithSingleObject()
        {
            var props = LdValue.BuildObject()
                .Add("property1", true)
                .Add("property2", "yes")
                .Build();

            var store = new DiagnosticStoreImpl();
            store._configProperties = new List<LdValue> { props };

            var e = store.InitEvent.Value.JsonValue;
            var configProps = e.Get("configuration");
            Assert.Equal(props, configProps);
        }

        [Fact]
        public void InitEventConfigPropertiesWithMergedObjects()
        {
            var props1 = LdValue.BuildObject()
                .Add("property1", true)
                .Add("property2", "yes")
                .Build();

            var props2 = LdValue.BuildObject()
                .Add("property3", 3)
                .Build();

            var allProps = LdValue.BuildObject()
                .Add("property1", true)
                .Add("property2", "yes")
                .Add("property3", 3)
                .Build();

            var store = new DiagnosticStoreImpl();
            store._configProperties = new List<LdValue> { props1, LdValue.Null, props2 };

            var e = store.InitEvent.Value.JsonValue;
            var configProps = e.Get("configuration");
            Assert.Equal(allProps, configProps);
        }
    }
}
