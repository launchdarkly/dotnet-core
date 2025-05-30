using System.Text.Json;
using Xunit;

namespace LaunchDarkly.Sdk
{
    public class SystemTextJsonInteroperabilityTest
    {
        // These tests verify that all of our LaunchDarkly.Sdk types that have a custom JSON conversion
        // behave correctly with System.Text.Json.

        // Keep these tests in sync with LdJsonNetTest.cs in LaunchDarkly.CommonSdk.JsonNet.Tests.

        private static readonly AttributeRef ExpectedAttributeRef = AttributeRef.FromLiteral("a");
        private const string ExpectedAttributeRefJson = @"""a""";
        private static readonly Context ExpectedContext = Context.New("user-key");
        private const string ExpectedContextJson = @"{""kind"":""user"",""key"":""user-key""}";
        private static readonly EvaluationReason ExpectedEvaluationReason = EvaluationReason.OffReason;
        private const string ExpectedEvaluationReasonJson = @"{""kind"":""OFF""}";
        private static readonly UnixMillisecondTime ExpectedUnixTime = UnixMillisecondTime.OfMillis(123456789);
        private const string ExpectedUnixTimeJson = "123456789";
        private static readonly User ExpectedUser = User.WithKey("user-key");
        private const string ExpectedUserJson = @"{""key"":""user-key""}";
        private static readonly LdValue ExpectedValue = LdValue.Of(true);
        private const string ExpectedValueJson = "true";

        // The reason for the "ObjectWithNullable..." classes is to test the serialization of nullable variants
        // of value types. For any value type T, if we pass a "T?" value to SerializeObject, the type of the
        // parameter in that method is actually "object" and so what is really passed is either a T or a plain
        // old null-- it doesn't really see a "T?". But if it's in a property like this, it really will detect
        // the type.

        private sealed class ObjectWithNullableAttributeRef
        {
            public AttributeRef? attr { get; set; }
        }

        private sealed class ObjectWithNullableContext
        {
            public Context? context { get; set; }
        }

        private sealed class ObjectWithNullableReason
        {
            public EvaluationReason? reason { get; set; }
        }

        private sealed class ObjectWithNullableTime
        {
            public UnixMillisecondTime? time { get; set; } // see above
        }

        private sealed class ObjectWithNullableValue
        {
            public LdValue? value { get; set; } // see above
            // "LdValue?" is a bit pointless, since an LdValue can already encode null, but it is a struct so this should work
        }

        [Fact]
        public void AttributeRefConversion() =>
            VerifySerializationAndDeserialization(ExpectedAttributeRef, ExpectedAttributeRefJson);

        [Fact]
        public void ContextConversion() =>
            VerifySerializationAndDeserialization(ExpectedContext, ExpectedContextJson);

        [Fact]
        public void EvaluationReasonConversion() =>
            VerifySerializationAndDeserialization(ExpectedEvaluationReason, ExpectedEvaluationReasonJson);

        [Fact]
        public void LdValueConversion() =>
            VerifySerializationAndDeserialization(ExpectedValue, ExpectedValueJson);

        [Fact]
        public void UnixMillisecondTimeConversion() =>
            VerifySerializationAndDeserialization(ExpectedUnixTime, ExpectedUnixTimeJson);

        [Fact]
        public void UserConversion() =>
            VerifySerializationAndDeserialization(ExpectedUser, ExpectedUserJson);

        [Fact]
        public void NullableValueTypes()
        {
            Assert.Equal(@"{""attr"":" + ExpectedAttributeRefJson + "}",
                JsonSerializer.Serialize(new ObjectWithNullableAttributeRef { attr = ExpectedAttributeRef }));
            Assert.Equal(@"{""attr"":null}",
                JsonSerializer.Serialize(new ObjectWithNullableAttributeRef { attr = null }));
            Assert.Equal(@"{""context"":" + ExpectedContextJson + "}",
                JsonSerializer.Serialize(new ObjectWithNullableContext { context = ExpectedContext }));
            Assert.Equal(@"{""context"":null}",
                JsonSerializer.Serialize(new ObjectWithNullableContext { context = null }));
            Assert.Equal(@"{""reason"":" + ExpectedEvaluationReasonJson + "}",
                JsonSerializer.Serialize(new ObjectWithNullableReason { reason = ExpectedEvaluationReason }));
            Assert.Equal(@"{""reason"":null}",
                JsonSerializer.Serialize(new ObjectWithNullableReason { reason = null }));
            Assert.Equal(@"{""time"":" + ExpectedUnixTimeJson + "}",
                JsonSerializer.Serialize(new ObjectWithNullableTime { time = ExpectedUnixTime }));
            Assert.Equal(@"{""time"":null}",
                JsonSerializer.Serialize(new ObjectWithNullableTime { time = null }));
            Assert.Equal(@"{""value"":" + ExpectedValueJson + "}",
                JsonSerializer.Serialize(new ObjectWithNullableValue { value = ExpectedValue }));
            Assert.Equal(@"{""value"":null}",
                JsonSerializer.Serialize(new ObjectWithNullableValue { value = null }));
        }

        private void VerifySerializationAndDeserialization<T>(T value, string expectedJson)
        {
            Assert.Equal(expectedJson, JsonSerializer.Serialize(value));
            Assert.Equal(value, JsonSerializer.Deserialize<T>(expectedJson));
        }
    }
}
