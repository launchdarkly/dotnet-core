using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Client.Interfaces;
using Xunit;

namespace LaunchDarkly.Sdk.Client
{
    public class ILdClientExtensionsTest
    {
        enum MyEnum
        {
            Red,
            Green,
            Blue
        };

        [Fact]
        public void EnumVariationConvertsStringToEnum()
        {
            var client = new MockStringVariationClient();
            client.SetupStringVariation("key", "Blue", "Green");

            var result = client.EnumVariation("key", MyEnum.Blue);
            Assert.Equal(MyEnum.Green, result);
        }

        [Fact]
        public void EnumVariationReturnsDefaultValueForInvalidFlagValue()
        {
            var client = new MockStringVariationClient();
            client.SetupStringVariation("key", "Blue", "not-a-color");

            var defaultValue = MyEnum.Blue;
            var result = client.EnumVariation("key", defaultValue);
            Assert.Equal(MyEnum.Blue, defaultValue);
        }

        [Fact]
        public void EnumVariationReturnsDefaultValueForNullFlagValue()
        {
            var client = new MockStringVariationClient();
            client.SetupStringVariation("key", "Blue", null);

            var defaultValue = MyEnum.Blue;
            var result = client.EnumVariation("key", defaultValue);
            Assert.Equal(defaultValue, result);
        }

        [Fact]
        public void EnumVariationDetailConvertsStringToEnum()
        {
            var client = new MockStringVariationClient();
            client.SetupStringVariationDetail("key", "Blue",
                new EvaluationDetail<string>("Green", 1, EvaluationReason.FallthroughReason));

            var result = client.EnumVariationDetail("key", MyEnum.Blue);
            var expected = new EvaluationDetail<MyEnum>(MyEnum.Green, 1, EvaluationReason.FallthroughReason);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void EnumVariationDetailReturnsDefaultValueForInvalidFlagValue()
        {
            var client = new MockStringVariationClient();
            client.SetupStringVariationDetail("key", "Blue",
                new EvaluationDetail<string>("not-a-color", 1, EvaluationReason.FallthroughReason));

            var result = client.EnumVariationDetail("key", MyEnum.Blue);
            var expected = new EvaluationDetail<MyEnum>(MyEnum.Blue, 1, EvaluationReason.ErrorReason(EvaluationErrorKind.WrongType));
            Assert.Equal(expected, result);
        }

        [Fact]
        public void EnumVariationDetailReturnsDefaultValueForNullFlagValue()
        {
            var client = new MockStringVariationClient();
            client.SetupStringVariationDetail("key", "Blue",
                new EvaluationDetail<string>(null, 1, EvaluationReason.FallthroughReason));

            var result = client.EnumVariationDetail("key", MyEnum.Blue);
            var expected = new EvaluationDetail<MyEnum>(MyEnum.Blue, 1, EvaluationReason.FallthroughReason);
            Assert.Equal(expected, result);
        }

        private sealed class MockStringVariationClient : ILdClient
        {
            private Func<string, string, string> _stringVariationFn;
            private Func<string, string, EvaluationDetail<string>> _stringVariationDetailFn;

            public void SetupStringVariation(string expectedKey, string expectedDefault, string result)
            {
                _stringVariationFn = (key, defaultValue) =>
                {
                    Assert.Equal(expectedKey, key);
                    Assert.Equal(expectedDefault, defaultValue);
                    return result;
                };
            }

            public void SetupStringVariationDetail(string expectedKey, string expectedDefault, EvaluationDetail<string> result)
            {
                _stringVariationDetailFn = (key, defaultValue) =>
                {
                    Assert.Equal(expectedKey, key);
                    Assert.Equal(expectedDefault, defaultValue);
                    return result;
                };
            }

            public string StringVariation(string key, string defaultValue) =>
                _stringVariationFn(key, defaultValue);

            public EvaluationDetail<string> StringVariationDetail(string key, string defaultValue) =>
                _stringVariationDetailFn(key, defaultValue);

            // Other methods aren't relevant to these tests

            public bool Initialized => true;
            public bool Offline => false;
            public IDataSourceStatusProvider DataSourceStatusProvider => null;
            public IFlagTracker FlagTracker => null;

            public IDictionary<string, LdValue> AllFlags() =>
                throw new System.NotImplementedException();

            public bool BoolVariation(string key, bool defaultValue = false) =>
                throw new System.NotImplementedException();

            public EvaluationDetail<bool> BoolVariationDetail(string key, bool defaultValue = false) =>
                throw new System.NotImplementedException();

            public void Dispose() { }

            public float FloatVariation(string key, float defaultValue = 0) =>
                throw new System.NotImplementedException();

            public EvaluationDetail<float> FloatVariationDetail(string key, float defaultValue = 0) =>
                throw new System.NotImplementedException();

            public double DoubleVariation(string key, double defaultValue = 0) =>
                throw new System.NotImplementedException();

            public EvaluationDetail<double> DoubleVariationDetail(string key, double defaultValue = 0) =>
                throw new System.NotImplementedException();

            public void Flush() { }

            public bool FlushAndWait(TimeSpan timeout) => true;

            public Task<bool> FlushAndWaitAsync(TimeSpan timeout) => Task.FromResult(true);

            public bool Identify(Context context, System.TimeSpan maxWaitTime) =>
                throw new System.NotImplementedException();

            public Task<bool> IdentifyAsync(Context context) =>
                throw new System.NotImplementedException();

            public int IntVariation(string key, int defaultValue = 0) =>
                throw new System.NotImplementedException();

            public EvaluationDetail<int> IntVariationDetail(string key, int defaultValue = 0) =>
                throw new System.NotImplementedException();

            public LdValue JsonVariation(string key, LdValue defaultValue) =>
                throw new System.NotImplementedException();

            public EvaluationDetail<LdValue> JsonVariationDetail(string key, LdValue defaultValue) =>
                throw new System.NotImplementedException();

            public bool SetOffline(bool value, System.TimeSpan maxWaitTime) =>
                throw new System.NotImplementedException();

            public Task SetOfflineAsync(bool value) =>
                throw new System.NotImplementedException();

            public void Track(string eventName) =>
                throw new System.NotImplementedException();

            public void Track(string eventName, LdValue data) =>
                throw new System.NotImplementedException();

            public void Track(string eventName, LdValue data, double metricValue) =>
                throw new System.NotImplementedException();
        }
    }
}
