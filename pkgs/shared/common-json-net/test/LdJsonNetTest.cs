using System.Collections.Generic;
using Newtonsoft.Json;
using Xunit;

namespace LaunchDarkly.Sdk.Json
{
    public class LdJsonNetTest
    {
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
        public void SerializeWithExplicitConverter()
        {
            Assert.Equal(ExpectedAttributeRefJson, JsonConvert.SerializeObject(ExpectedAttributeRef, LdJsonNet.Converter));
            Assert.Equal(ExpectedContextJson, JsonConvert.SerializeObject(ExpectedContext, LdJsonNet.Converter));
            Assert.Equal(ExpectedEvaluationReasonJson, JsonConvert.SerializeObject(ExpectedEvaluationReason, LdJsonNet.Converter));
            Assert.Equal(ExpectedUnixTimeJson, JsonConvert.SerializeObject(ExpectedUnixTime, LdJsonNet.Converter));
            Assert.Equal(ExpectedUserJson, JsonConvert.SerializeObject(ExpectedUser, LdJsonNet.Converter));
            Assert.Equal(ExpectedValueJson, JsonConvert.SerializeObject(ExpectedValue, LdJsonNet.Converter));
        }

        [Fact]
        public void SerializeWithConverterInSettings()
        {
            var settings = new JsonSerializerSettings
            {
                Converters = new List<JsonConverter> { LdJsonNet.Converter }
            };
            Assert.Equal(ExpectedAttributeRefJson, JsonConvert.SerializeObject(ExpectedAttributeRef, settings));
            Assert.Equal(ExpectedContextJson, JsonConvert.SerializeObject(ExpectedContext, settings));
            Assert.Equal(ExpectedEvaluationReasonJson, JsonConvert.SerializeObject(ExpectedEvaluationReason, settings));
            Assert.Equal(ExpectedUnixTimeJson, JsonConvert.SerializeObject(ExpectedUnixTime, settings));
            Assert.Equal(ExpectedUserJson, JsonConvert.SerializeObject(ExpectedUser, settings));
            Assert.Equal(ExpectedValueJson, JsonConvert.SerializeObject(ExpectedValue, settings));
        }

        [Fact]
        public void DeserializeWithExplicitConverter()
        {
            Assert.Equal(ExpectedAttributeRef, JsonConvert.DeserializeObject<AttributeRef>(ExpectedAttributeRefJson, LdJsonNet.Converter));
            Assert.Equal(ExpectedContext, JsonConvert.DeserializeObject<Context>(ExpectedContextJson, LdJsonNet.Converter));
            Assert.Equal(ExpectedEvaluationReason,
                JsonConvert.DeserializeObject<EvaluationReason>(ExpectedEvaluationReasonJson, LdJsonNet.Converter));
            Assert.Equal(ExpectedUnixTime, JsonConvert.DeserializeObject<UnixMillisecondTime>(ExpectedUnixTimeJson, LdJsonNet.Converter));
            Assert.Equal(ExpectedUser, JsonConvert.DeserializeObject<User>(ExpectedUserJson, LdJsonNet.Converter));
            Assert.Equal(ExpectedValue, JsonConvert.DeserializeObject<LdValue>(ExpectedValueJson, LdJsonNet.Converter));
        }

        [Fact]
        public void DeserializeWithConverterInSettings()
        {
            var settings = new JsonSerializerSettings
            {
                Converters = new List<JsonConverter> { LdJsonNet.Converter }
            };
            Assert.Equal(ExpectedAttributeRef, JsonConvert.DeserializeObject<AttributeRef>(ExpectedAttributeRefJson, settings));
            Assert.Equal(ExpectedContext, JsonConvert.DeserializeObject<Context>(ExpectedContextJson, settings));
            Assert.Equal(ExpectedEvaluationReason,
                JsonConvert.DeserializeObject<EvaluationReason>(ExpectedEvaluationReasonJson, settings));
            Assert.Equal(ExpectedUnixTime, JsonConvert.DeserializeObject<UnixMillisecondTime>(ExpectedUnixTimeJson, settings));
            Assert.Equal(ExpectedUser, JsonConvert.DeserializeObject<User>(ExpectedUserJson, settings));
            Assert.Equal(ExpectedValue, JsonConvert.DeserializeObject<LdValue>(ExpectedValueJson, settings));
        }

        [Fact]
        public void NullableValueTypeIsSerializedCorrectly()
        {
            Assert.Equal(@"{""attr"":" + ExpectedAttributeRefJson + "}",
                JsonConvert.SerializeObject(new ObjectWithNullableAttributeRef { attr = ExpectedAttributeRef }, LdJsonNet.Converter));
            Assert.Equal(@"{""attr"":null}",
                JsonConvert.SerializeObject(new ObjectWithNullableAttributeRef { attr = null }, LdJsonNet.Converter));
            Assert.Equal(@"{""context"":" + ExpectedContextJson + "}",
                JsonConvert.SerializeObject(new ObjectWithNullableContext { context = ExpectedContext }, LdJsonNet.Converter));
            Assert.Equal(@"{""context"":null}",
                JsonConvert.SerializeObject(new ObjectWithNullableContext { context = null }, LdJsonNet.Converter));
            Assert.Equal(@"{""reason"":" + ExpectedEvaluationReasonJson + "}",
                JsonConvert.SerializeObject(new ObjectWithNullableReason { reason = ExpectedEvaluationReason }, LdJsonNet.Converter));
            Assert.Equal(@"{""reason"":null}",
                JsonConvert.SerializeObject(new ObjectWithNullableReason { reason = null }, LdJsonNet.Converter));
            Assert.Equal(@"{""time"":" + ExpectedUnixTimeJson + "}",
                JsonConvert.SerializeObject(new ObjectWithNullableTime { time = ExpectedUnixTime }, LdJsonNet.Converter));
            Assert.Equal(@"{""time"":null}",
                JsonConvert.SerializeObject(new ObjectWithNullableTime { time = null }, LdJsonNet.Converter));
            Assert.Equal(@"{""value"":" + ExpectedValueJson + "}",
                JsonConvert.SerializeObject(new ObjectWithNullableValue { value = ExpectedValue }, LdJsonNet.Converter));
            Assert.Equal(@"{""value"":null}",
                JsonConvert.SerializeObject(new ObjectWithNullableValue { value = null }, LdJsonNet.Converter));
        }

        [Fact]
        public void NullableValueTypeIsDeserializedCorrectly()
        {
            Assert.Equal(ExpectedAttributeRef,
                JsonConvert.DeserializeObject<ObjectWithNullableAttributeRef>(
                    @"{""attr"":" + ExpectedAttributeRefJson + "}",
                    LdJsonNet.Converter).attr);
            Assert.Equal(ExpectedContext,
                JsonConvert.DeserializeObject<ObjectWithNullableContext>(
                    @"{""context"":" + ExpectedContextJson + "}",
                    LdJsonNet.Converter).context);
            Assert.Equal(ExpectedEvaluationReason,
                JsonConvert.DeserializeObject<ObjectWithNullableReason>(
                    @"{""reason"":" + ExpectedEvaluationReasonJson + "}",
                    LdJsonNet.Converter).reason);
            Assert.Null(JsonConvert.DeserializeObject<ObjectWithNullableReason>(
                @"{""reason"":null}",
                LdJsonNet.Converter).reason);
            Assert.Equal(ExpectedUnixTime,
                JsonConvert.DeserializeObject<ObjectWithNullableTime>(
                    @"{""time"":" + ExpectedUnixTimeJson + "}",
                    LdJsonNet.Converter).time);
            Assert.Null(JsonConvert.DeserializeObject<ObjectWithNullableTime>(
                @"{""time"":null}",
                LdJsonNet.Converter).time);
        }
    }
}
