using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

using static LaunchDarkly.Sdk.Internal.JsonConverterHelpers;

namespace LaunchDarkly.Sdk.Internal
{
    public class JsonConverterHelpersTest
    {
        private static byte[] BytesOf(string s) => Encoding.UTF8.GetBytes(s);
        private static Utf8JsonReader PrepareReader(string s)
        {
            var r = new Utf8JsonReader(BytesOf(s));
            r.Read();
            return r;
        }

        [Fact]
        public void TestGetIntOrNull()
        {
            var r1 = PrepareReader("3");
            Assert.Equal(3, GetIntOrNull(ref r1));

            var r2 = PrepareReader("null");
            Assert.Null(GetIntOrNull(ref r2));

            var r3 = PrepareReader("true");
            try
            {
                GetIntOrNull(ref r3);
                Assert.Fail("expected exception");
            }
            catch (InvalidOperationException) { }
        }

        [Fact]
        public void TestGetLongOrNull()
        {
            var r1 = PrepareReader("300000000000000");
            Assert.Equal(300000000000000, GetLongOrNull(ref r1));

            var r2 = PrepareReader("null");
            Assert.Null(GetLongOrNull(ref r2));

            var r3 = PrepareReader("true");
            try
            {
                GetLongOrNull(ref r3);
                Assert.Fail("expected exception");
            }
            catch (InvalidOperationException) { }
        }

        [Fact]
        public void TestGetTimeOrNull()
        {
            var r1 = PrepareReader("300000000000000");
            Assert.Equal(UnixMillisecondTime.OfMillis(300000000000000), GetTimeOrNull(ref r1));

            var r2 = PrepareReader("null");
            Assert.Null(GetTimeOrNull(ref r2));

            var r3 = PrepareReader("true");
            try
            {
                GetTimeOrNull(ref r3);
                Assert.Fail("expected exception");
            }
            catch (InvalidOperationException) { }
        }

        [Fact]
        public void TestWriteJsonAsString()
        {
            Assert.Equal("true", WriteJsonAsString(w => w.WriteBooleanValue(true)));
        }

        [Fact]
        public void TestWriteIntIfNotNull()
        {
            Assert.Equal(@"{""a"":3}", WriteJsonAsString(w =>
            {
                w.WriteStartObject();
                WriteIntIfNotNull(w, "a", 3);
                w.WriteEndObject();
            }));
            Assert.Equal(@"{}", WriteJsonAsString(w =>
            {
                w.WriteStartObject();
                WriteIntIfNotNull(w, "a", null);
                w.WriteEndObject();
            }));
        }

        [Fact]
        public void TestWriteTimeIfNotNull()
        {
            Assert.Equal(@"{""a"":300000000000000}", WriteJsonAsString(w =>
            {
                w.WriteStartObject();
                WriteTimeIfNotNull(w, "a", UnixMillisecondTime.OfMillis(300000000000000));
                w.WriteEndObject();
            }));
            Assert.Equal(@"{}", WriteJsonAsString(w =>
            {
                w.WriteStartObject();
                WriteTimeIfNotNull(w, "a", null);
                w.WriteEndObject();
            }));
        }

        [Fact]
        public void TestWriteStringIfNotNull()
        {
            Assert.Equal(@"{""a"":""""}", WriteJsonAsString(w =>
            {
                w.WriteStartObject();
                WriteStringIfNotNull(w, "a", "");
                w.WriteEndObject();
            }));
            Assert.Equal(@"{}", WriteJsonAsString(w =>
            {
                w.WriteStartObject();
                WriteStringIfNotNull(w, "a", null);
                w.WriteEndObject();
            }));
        }

        [Fact]
        public void TestWriteBooleanIfTrue()
        {
            Assert.Equal(@"{""a"":true}", WriteJsonAsString(w =>
            {
                w.WriteStartObject();
                WriteBooleanIfTrue(w, "a", true);
                w.WriteEndObject();
            }));
            Assert.Equal(@"{}", WriteJsonAsString(w =>
            {
                w.WriteStartObject();
                WriteBooleanIfTrue(w, "a", false);
                w.WriteEndObject();
            }));
        }

        [Fact]
        public void TestWriteLdValue()
        {
            Assert.Equal(@"{""a"":true,""b"":null}", WriteJsonAsString(w =>
            {
                w.WriteStartObject();
                WriteLdValue(w, "a", LdValue.Of(true));
                WriteLdValue(w, "b", LdValue.Null);
                w.WriteEndObject();
            }));
        }

        [Fact]
        public void TestWriteLdValueIfNotNull()
        {
            Assert.Equal(@"{""a"":true}", WriteJsonAsString(w =>
            {
                w.WriteStartObject();
                WriteLdValueIfNotNull(w, "a", LdValue.Of(true));
                WriteLdValueIfNotNull(w, "b", LdValue.Null);
                w.WriteEndObject();
            }));
        }

        [Fact]
        public void NonEmptyArray()
        {
            var bytes = BytesOf(@"[10,20]");

            var r1 = new Utf8JsonReader(bytes);
            var a1 = RequireArray(ref r1);
            VerifyNonEmptyArray(ref r1, ref a1);

            var r2 = new Utf8JsonReader(bytes);
            var a2 = RequireArrayOrNull(ref r2);
            VerifyNonEmptyArray(ref r2, ref a2);
        }

        private void VerifyNonEmptyArray(scoped ref Utf8JsonReader r, ref ArrayHelper a)
        {
            Assert.True(a.Next(ref r));
            Assert.Equal(10, r.GetInt32());
            Assert.True(a.Next(ref r));
            Assert.Equal(20, r.GetInt32());
            Assert.False(a.Next(ref r));
        }

        [Fact]
        public void EmptyArray()
        {
            var bytes = BytesOf(@"[]");

            var r1 = new Utf8JsonReader(bytes);
            var a1 = RequireArray(ref r1);
            VerifyEmptyArray(ref r1, ref a1);

            var r2 = new Utf8JsonReader(bytes);
            var a2 = RequireArrayOrNull(ref r2);
            VerifyEmptyArray(ref r2, ref a2);
        }

        private void VerifyEmptyArray(scoped ref Utf8JsonReader r, ref ArrayHelper a) =>
            Assert.False(a.Next(ref r));

        [Fact]
        public void NullInsteadOfArray()
        {
            var bytes = BytesOf(@"null");

            var r1 = new Utf8JsonReader(bytes);
            try
            {
                RequireArray(ref r1);
                Assert.Fail("expected exception");
            }
            catch (JsonException) { }

            var r2 = new Utf8JsonReader(bytes);
            var a2 = RequireArrayOrNull(ref r2);
            VerifyEmptyArray(ref r2, ref a2);
        }

        [Fact]
        public void NonEmptyObject()
        {
            var bytes = BytesOf(@"{""a"":10,""b"":20}");

            var r1 = new Utf8JsonReader(bytes);
            var o1 = RequireObject(ref r1);
            VerifyNonEmptyObject(ref r1, ref o1);

            var r2 = new Utf8JsonReader(bytes);
            var o2 = RequireObjectOrNull(ref r2);
            VerifyNonEmptyObject(ref r2, ref o2);
        }

        private void VerifyNonEmptyObject(scoped ref Utf8JsonReader r, ref ObjectHelper o)
        {
            Assert.True(o.Next(ref r));
            Assert.Equal("a", o.Name);
            Assert.Equal(10, r.GetInt32());
            Assert.True(o.Next(ref r));
            Assert.Equal("b", o.Name);
            Assert.Equal(20, r.GetInt32());
            Assert.False(o.Next(ref r));
        }

        [Fact]
        public void EmptyObject()
        {
            var bytes = BytesOf(@"{}");

            var r1 = new Utf8JsonReader(bytes);
            var o1 = RequireObject(ref r1);
            VerifyEmptyObject(ref r1, ref o1);

            var r2 = new Utf8JsonReader(bytes);
            var o2 = RequireObjectOrNull(ref r2);
            VerifyEmptyObject(ref r2, ref o2);
        }

        private void VerifyEmptyObject(scoped ref Utf8JsonReader r, ref ObjectHelper o) =>
            Assert.False(o.Next(ref r));

        [Fact]
        public void NullInsteadOfObject()
        {
            var bytes = BytesOf("null");

            var r1 = new Utf8JsonReader(bytes);
            try
            {
                RequireObject(ref r1);
                Assert.Fail("expected exception");
            }
            catch (JsonException) { }

            var r2 = new Utf8JsonReader(bytes);
            var o2 = RequireObjectOrNull(ref r2);
            VerifyEmptyObject(ref r2, ref o2);
        }

        [Fact]
        public void ObjectPropertyValueIsSkippedIfNotConsumed()
        {
            var bytes = BytesOf(@"{""a"":1,""b"":{""x"":99},""c"":2}");

            var r = new Utf8JsonReader(bytes);
            var obj = RequireObject(ref r);
            Assert.True(obj.Next(ref r));
            Assert.Equal("a", obj.Name);
            Assert.Equal(1, r.GetInt32());
            Assert.True(obj.Next(ref r));
            Assert.Equal("b", obj.Name);
            // deliberately do not consume the value for b; it should be automatically skipped by the next Next
            Assert.True(obj.Next(ref r));
            Assert.Equal("c", obj.Name);
            Assert.Equal(2, r.GetInt32());
        }

        [Fact]
        public void RequiredPropertiesAreAllFound()
        {
            var bytes = BytesOf(@"{""a"":1, ""b"":2, ""c"":3}");

            var r = new Utf8JsonReader(bytes);
            var obj = RequireObject(ref r).WithRequiredProperties("c", "b", "a");
            while (obj.Next(ref r)) { } // no error is thrown
        }

        [Fact]
        public void RequiredPropertyIsNotFound()
        {
            var bytes = BytesOf(@"{""b"":2, ""c"":3}");

            var r = new Utf8JsonReader(bytes);
            var obj = RequireObject(ref r).WithRequiredProperties("c", "b", "a");
            try
            {
                while (obj.Next(ref r)) { }
                Assert.Fail("expected exception");
            }
            catch (JsonException e)
            {
                Assert.Matches(".*required property: a", e.Message);
            }
        }
    }
}

