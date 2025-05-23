using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using LaunchDarkly.Sdk.Json;
using LaunchDarkly.TestHelpers;
using Xunit;

namespace LaunchDarkly.Sdk
{
    public class LdValueTest
    {
        const int someInt = 3;
        const long someLong = 3;
        const float someFloat = 3.25f;
        const double someDouble = 3.25d;
        const string someString = "hi";

        static readonly LdValue aTrueBoolValue = LdValue.Of(true);
        static readonly LdValue anIntValue = LdValue.Of(someInt);
        static readonly LdValue aLongValue = LdValue.Of(someLong);
        static readonly LdValue aFloatValue = LdValue.Of(someFloat);
        static readonly LdValue aDoubleValue = LdValue.Of(someDouble);
        static readonly LdValue aStringValue = LdValue.Of(someString);
        static readonly LdValue aNumericLookingStringValue = LdValue.Of("3");
        static readonly LdValue anArrayValue = LdValue.Convert.Int.ArrayOf(3);
        static readonly LdValue anObjectValue = LdValue.Convert.String.ObjectFrom(MakeDictionary("x"));
        
        [Fact]
        public void CanGetValueAsBool()
        {
            Assert.Equal(LdValueType.Bool, aTrueBoolValue.Type);
            Assert.True(aTrueBoolValue.AsBool);
            Assert.True(LdValue.Convert.Bool.ToType(aTrueBoolValue));
        }

        [Fact]
        public void NonBooleanValueAsBoolIsFalse()
        {
            var values = new LdValue[]
            {
                LdValue.Null,
                aStringValue,
                anIntValue,
                aLongValue,
                aFloatValue,
                aDoubleValue,
                anArrayValue,
                anObjectValue
            };
            foreach (var value in values)
            {
                Assert.False(value.AsBool);
                Assert.False(LdValue.Convert.Bool.ToType(value));
            }
        }

        [Fact]
        public void CanGetValueAsString()
        {
            Assert.Equal(LdValueType.String, aStringValue.Type);
            Assert.Equal(someString, aStringValue.AsString);
            Assert.Equal(someString, LdValue.Convert.String.ToType(aStringValue));
        }

        [Fact]
        public void NonStringValueAsStringIsNull()
        {
            var values = new LdValue[]
            {
                LdValue.Null,
                aTrueBoolValue,
                anIntValue,
                aFloatValue,
                anArrayValue,
                anObjectValue
            };
            foreach (var value in values)
            {
                Assert.NotEqual(LdValueType.String, value.Type);
                Assert.Null(value.AsString);
                Assert.Null(LdValue.Convert.String.ToType(value));
            }
        }
        
        [Fact]
        public void CanGetIntegerValueOfAnyNumericType()
        {
            TestConvertIntegerToNumericType(LdValue.Convert.Int, v => v.AsInt);
            TestConvertIntegerToNumericType(LdValue.Convert.Long, v => v.AsLong);
            TestConvertIntegerToNumericType(LdValue.Convert.Float, v => v.AsFloat);
            TestConvertIntegerToNumericType(LdValue.Convert.Double, v => v.AsDouble);
            Assert.Equal(LdValueType.Number, anIntValue.Type);
            Assert.Equal(LdValueType.Number, aLongValue.Type);
        }

        private void TestConvertIntegerToNumericType<T>(LdValue.Converter<T> converter, Func<LdValue, T> getter)
        {
            var t_2 = (T)Convert.ChangeType(2, typeof(T));
            TestTypeConversion((int)2, t_2, n => LdValue.Of(n), converter, getter);
            TestTypeConversion((long)2, t_2, n => LdValue.Of(n), converter, getter);
            TestTypeConversion((float)2, t_2, n => LdValue.Of(n), converter, getter);
            TestTypeConversion((double)2, t_2, n => LdValue.Of(n), converter, getter);
        }

        [Fact]
        public void NonIntegerValueAsIntegerRoundsToNearest()
        {
            TestConvertNonIntegerToIntegerType(LdValue.Convert.Int, v => v.AsInt);
            TestConvertNonIntegerToIntegerType(LdValue.Convert.Long, v => v.AsLong);
        }

        private void TestConvertNonIntegerToIntegerType<T>(LdValue.Converter<T> converter, Func<LdValue, T> getter)
        {
            var t_2 = (T)Convert.ChangeType(2, typeof(T));
            var t_3 = (T)Convert.ChangeType(3, typeof(T));
            var t_minus_2 = (T)Convert.ChangeType(-2, typeof(T));
            var t_minus_3 = (T)Convert.ChangeType(-3, typeof(T));
            TestTypeConversion((float)2.25, t_2, n => LdValue.Of(n), converter, getter);
            TestTypeConversion((double)2.25, t_2, n => LdValue.Of(n), converter, getter);
            TestTypeConversion((float)2.75, t_3, n => LdValue.Of(n), converter, getter);
            TestTypeConversion((double)2.75, t_3, n => LdValue.Of(n), converter, getter);
            TestTypeConversion((float)-2.25, t_minus_2, n => LdValue.Of(n), converter, getter);
            TestTypeConversion((double)-2.25, t_minus_2, n => LdValue.Of(n), converter, getter);
            TestTypeConversion((float)-2.75, t_minus_3, n => LdValue.Of(n), converter, getter);
            TestTypeConversion((double)-2.75, t_minus_3, n => LdValue.Of(n), converter, getter);
        }
        
        [Fact]
        public void CanGetNonIntegerValueAsFloatingPoint()
        {
            TestTypeConversion(2.5f, 2.5f, n => LdValue.Of(n), LdValue.Convert.Float, v => v.AsFloat);
            TestTypeConversion(2.5d, 2.5f, n => LdValue.Of(n), LdValue.Convert.Float, v => v.AsFloat);
            TestTypeConversion(2.5d, 2.5d, n => LdValue.Of(n), LdValue.Convert.Double, v => v.AsDouble);
            TestTypeConversion(2.5d, 2.5d, n => LdValue.Of(n), LdValue.Convert.Double, v => v.AsDouble);
            Assert.Equal(LdValueType.Number, aFloatValue.Type);
            Assert.Equal(LdValueType.Number, aDoubleValue.Type);
        }
        
        [Fact]
        public void NonNumericValueAsNumberIsZero()
        {
            TestNonNumericValueAsNumericTypeIsZero(LdValue.Convert.Int, v => v.AsInt, 0);
            TestNonNumericValueAsNumericTypeIsZero(LdValue.Convert.Long, v => v.AsLong, 0);
            TestNonNumericValueAsNumericTypeIsZero(LdValue.Convert.Float, v => v.AsFloat, 0);
            TestNonNumericValueAsNumericTypeIsZero(LdValue.Convert.Double, v => v.AsDouble, 0);
        }
        
        private void TestNonNumericValueAsNumericTypeIsZero<T>(LdValue.Converter<T> converter,
            Func<LdValue, T> getter, T zero)
        {
            var values = new LdValue[]
            {
                LdValue.Null,
                aTrueBoolValue,
                aStringValue,
                aNumericLookingStringValue,
                anArrayValue,
                anObjectValue
            };
            foreach (var value in values)
            {
                Assert.Equal(zero, getter(value));
                Assert.Equal(zero, converter.ToType(value));
            }
        }

        private void TestTypeConversion<T, U>(T fromValue, U toValue, Func<T, LdValue> constructor,
            LdValue.Converter<U> converter, Func<LdValue, U> getter)
        {
            var value = constructor(fromValue);
            Assert.Equal(toValue, getter(value));
            Assert.Equal(toValue, converter.ToType(value));
        }
        
        [Fact]
        public void CanGetValuesAsList()
        {
            Assert.Equal(new LdValue[] { LdValue.Of("x"), LdValue.Of(true) },
                LdValue.BuildArray().Add("x").Add(true).Build().List);
            Assert.Equal(new bool[] { true, false }, LdValue.Convert.Bool.ArrayFrom(new bool[] { true, false }).AsList(LdValue.Convert.Bool));
            Assert.Equal(new bool[] { true, false }, LdValue.Convert.Bool.ArrayOf(true, false).AsList(LdValue.Convert.Bool));
            Assert.Equal(new bool[] { true, false }, LdValue.BuildArray().Add(true).Add(false).Build().AsList(LdValue.Convert.Bool));
            Assert.Equal(new int[] { 1, 2 }, LdValue.Convert.Int.ArrayFrom(new int[] { 1, 2 }).AsList(LdValue.Convert.Int));
            Assert.Equal(new int[] { 1, 2 }, LdValue.Convert.Int.ArrayOf(1, 2).AsList(LdValue.Convert.Int));
            Assert.Equal(new int[] { 1, 2 }, LdValue.BuildArray().Add(1).Add(2).Build().AsList(LdValue.Convert.Int));
            Assert.Equal(new long[] { 1, 2 }, LdValue.Convert.Long.ArrayFrom(new long[] { 1, 2 }).AsList(LdValue.Convert.Long));
            Assert.Equal(new long[] { 1, 2 }, LdValue.Convert.Long.ArrayOf(1, 2).AsList(LdValue.Convert.Long));
            Assert.Equal(new long[] { 1, 2 }, LdValue.BuildArray().Add(1).Add(2).Build().AsList(LdValue.Convert.Long));
            Assert.Equal(new float[] { 1.0f, 2.0f }, LdValue.Convert.Float.ArrayFrom(new float[] { 1.0f, 2.0f }).AsList(LdValue.Convert.Float));
            Assert.Equal(new float[] { 1.0f, 2.0f }, LdValue.Convert.Float.ArrayOf(1.0f, 2.0f).AsList(LdValue.Convert.Float));
            Assert.Equal(new float[] { 1.0f, 2.0f }, LdValue.BuildArray().Add(1.0f).Add(2.0f).Build().AsList(LdValue.Convert.Float));
            Assert.Equal(new double[] { 1.0d, 2.0d }, LdValue.Convert.Double.ArrayFrom(new double[] { 1.0d, 2.0d }).AsList(LdValue.Convert.Double));
            Assert.Equal(new double[] { 1.0d, 2.0d }, LdValue.Convert.Double.ArrayOf(1.0d, 2.0d).AsList(LdValue.Convert.Double));
            Assert.Equal(new double[] { 1.0f, 2.0f }, LdValue.BuildArray().Add(1.0d).Add(2.0d).Build().AsList(LdValue.Convert.Double));
            Assert.Equal(new string[] { "a", "b" }, LdValue.Convert.String.ArrayFrom(new string[] { "a", "b" }).AsList(LdValue.Convert.String));
            Assert.Equal(new string[] { "a", "b" }, LdValue.Convert.String.ArrayOf("a", "b").AsList(LdValue.Convert.String));
            Assert.Equal(new string[] { "a", "b" }, LdValue.BuildArray().Add("a").Add("b").Build().AsList(LdValue.Convert.String));
            Assert.Equal(new LdValue[] { anIntValue, aStringValue },
                LdValue.ArrayFrom(new LdValue[] { anIntValue, aStringValue }).AsList(LdValue.Convert.Json));
            Assert.Equal(new LdValue[] { anIntValue, aStringValue },
                LdValue.ArrayOf(anIntValue, aStringValue).AsList(LdValue.Convert.Json));
            Assert.Equal(new LdValue[] { anIntValue, aStringValue },
                LdValue.BuildArray().Add(anIntValue).Add(aStringValue).Build().AsList(LdValue.Convert.Json));
            Assert.Equal(LdValue.Null, LdValue.Convert.Int.ArrayFrom((IEnumerable<int>)null));
        }
        
        [Fact]
        public void ListCanGetItemByIndex()
        {
            var v = LdValue.Convert.Int.ArrayOf(1, 2, 3);

            Assert.Equal(3, v.Count);
            Assert.Equal(LdValue.Of(2), v.Get(1));
            Assert.Equal(LdValue.Null, v.Get(-1));
            Assert.Equal(LdValue.Null, v.Get(3));

            var list = v.AsList(LdValue.Convert.Int);
            Assert.Equal(2, list[1]);
            Assert.Throws<IndexOutOfRangeException>(() => list[-1]);
            Assert.Throws<IndexOutOfRangeException>(() => list[3]);
        }

        [Fact]
        public void ListCanBeEnumerated()
        {
            var v = LdValue.Convert.Int.ArrayOf(1, 2, 3);
            var list = v.AsList(LdValue.Convert.Int);
            var listOut = new List<int>();
            Assert.Equal(3, list.Count);
            foreach (var n in list)
            {
                listOut.Add(n);
            }
            Assert.Equal(listOut, list);
        }

        [Fact]
        public void NonArrayValuesReturnEmptyOrList()
        {
            var values = new LdValue[]
            {
                LdValue.Null,
                aTrueBoolValue,
                anIntValue,
                aFloatValue,
                aStringValue,
                anObjectValue
            };
            foreach (var value in values)
            {
                Assert.Empty(value.List);

                // use list conversion
                var emptyList = value.AsList(LdValue.Convert.Bool);
                Assert.Equal(0, emptyList.Count);
                Assert.Throws<IndexOutOfRangeException>(() => emptyList[0]);
                foreach (var n in emptyList)
                {
                    Assert.True(false, "should not have enumerated an element");
                }
            }
        }

        [Fact]
        public void PrimitiveTypesCannotBeEnumerated()
        {
            var values = new LdValue[]
            {
                LdValue.Null,
                aTrueBoolValue,
                anIntValue,
                aFloatValue,
                aStringValue
            };
            foreach (var value in values)
            {
                Assert.Equal(0, value.Count);
                Assert.Equal(LdValue.Null, value.Get(0));
                Assert.Equal(LdValue.Null, value.Get(-1));
            }
        }

        [Fact]
        public void CanGetValueAsDictionary()
        {
            AssertDictsEqual(MakeDictionary(LdValue.Of("x"), LdValue.Of(true)),
                LdValue.BuildObject().Add("1", "x").Add("2", true).Build().Dictionary);
            AssertDictsEqual(MakeDictionary(true, false),
                LdValue.Convert.Bool.ObjectFrom(MakeDictionary(true, false)).AsDictionary(LdValue.Convert.Bool));
            AssertDictsEqual(MakeDictionary(true, false),
                LdValue.BuildObject().Add("1", true).Add("2", false).Build().AsDictionary(LdValue.Convert.Bool));
            AssertDictsEqual(MakeDictionary(1, 2),
                LdValue.Convert.Int.ObjectFrom(MakeDictionary(1, 2)).AsDictionary(LdValue.Convert.Int));
            AssertDictsEqual(MakeDictionary(1, 2),
                LdValue.BuildObject().Add("1", 1).Add("2", 2).Build().AsDictionary(LdValue.Convert.Int));
            AssertDictsEqual(MakeDictionary(1L, 2L),
                LdValue.Convert.Long.ObjectFrom(MakeDictionary(1L, 2L)).AsDictionary(LdValue.Convert.Long));
            AssertDictsEqual(MakeDictionary(1L, 2L),
                LdValue.BuildObject().Add("1", 1L).Add("2", 2L).Build().AsDictionary(LdValue.Convert.Long));
            AssertDictsEqual(MakeDictionary(1.0f, 2.0f),
                LdValue.Convert.Float.ObjectFrom(MakeDictionary(1.0f, 2.0f)).AsDictionary(LdValue.Convert.Float));
            AssertDictsEqual(MakeDictionary(1.0f, 2.0f),
                LdValue.BuildObject().Add("1", 1.0f).Add("2", 2.0f).Build().AsDictionary(LdValue.Convert.Float));
            AssertDictsEqual(MakeDictionary(1.0d, 2.0d),
                LdValue.Convert.Double.ObjectFrom(MakeDictionary(1.0d, 2.0d)).AsDictionary(LdValue.Convert.Double));
            AssertDictsEqual(MakeDictionary(1.0d, 2.0d),
                LdValue.BuildObject().Add("1", 1.0d).Add("2", 2.0d).Build().AsDictionary(LdValue.Convert.Double));
            AssertDictsEqual(MakeDictionary("a", "b"),
                LdValue.Convert.String.ObjectFrom(MakeDictionary("a", "b")).AsDictionary(LdValue.Convert.String));
            AssertDictsEqual(MakeDictionary("a", "b"),
                LdValue.BuildObject().Add("1", "a").Add("2", "b").Build().AsDictionary(LdValue.Convert.String));
            AssertDictsEqual(MakeDictionary(anIntValue, aStringValue),
                LdValue.Convert.Json.ObjectFrom(MakeDictionary(anIntValue, aStringValue)).AsDictionary(LdValue.Convert.Json));
            AssertDictsEqual(MakeDictionary(anIntValue, aStringValue),
                LdValue.BuildObject().Add("1", anIntValue).Add("2", aStringValue).Build().AsDictionary(LdValue.Convert.Json));
            Assert.Equal(LdValue.Null, LdValue.Convert.String.ObjectFrom((IReadOnlyDictionary<string, string>)null));
        }

        [Fact]
        public void DictionaryCanGetValueByKey()
        {
            var v = LdValue.BuildObject().Add("a", 100).Add("b", 200).Add("c", 300).Build();

            Assert.Equal(3, v.Count);
            Assert.Equal(LdValue.Of(200), v.Get("b"));
            Assert.NotEqual(LdValue.Null, v.Get(1));
            Assert.Equal(LdValue.Null, v.Get("x"));
            Assert.Equal(LdValue.Null, v.Get(-1));
            Assert.Equal(LdValue.Null, v.Get(3));

            var d = v.AsDictionary(LdValue.Convert.Int);
            Assert.True(d.ContainsKey("b"));
            Assert.Equal(200, d["b"]);
            Assert.True(d.TryGetValue("a", out var n));
            Assert.Equal(100, n);
            Assert.False(d.ContainsKey("x"));
            Assert.Throws<KeyNotFoundException>(() => d["x"]);
            Assert.False(d.TryGetValue("x", out n));
        }

        [Fact]
        public void DictionaryCanBeEnumerated()
        {
            var v = LdValue.BuildObject().Add("a", 100).Add("b", 200).Add("c", 300).Build();
            var d = v.AsDictionary(LdValue.Convert.Int);
            Assert.Equal(3, d.Count);
            Assert.Equal(new string[] { "a", "b", "c" }, new List<string>(d.Keys).OrderBy(s => s));
            Assert.Equal(new int[] { 100, 200, 300 }, new List<int>(d.Values).OrderBy(n => n));
            Assert.Equal(new KeyValuePair<string, int>[]
            {
                new KeyValuePair<string, int>("a", 100),
                new KeyValuePair<string, int>("b", 200),
                new KeyValuePair<string, int>("c", 300),
            }, d.OrderBy(e => e.Key));
        }

        [Fact]
        public void NonObjectValuesReturnEmptyDictionary()
        {
            var values = new LdValue[]
            {
                LdValue.Null,
                aTrueBoolValue,
                anIntValue,
                aFloatValue,
                aStringValue,
                anArrayValue
            };
            foreach (var value in values)
            {
                Assert.Equal(LdValue.Null, value.Get("1"));

                Assert.Empty(value.Dictionary);

                var emptyDict = value.AsDictionary(LdValue.Convert.Bool);
                Assert.Equal(0, emptyDict.Count);
                Assert.False(emptyDict.ContainsKey("1"));
                Assert.Throws<KeyNotFoundException>(() => emptyDict["1"]);
                Assert.False(emptyDict.TryGetValue("1", out var b));
                foreach (var e in emptyDict)
                {
                    Assert.True(false, "should not have enumerated an element");
                }
                Assert.Equal(new string[0], emptyDict.Keys);
                Assert.Equal(new bool[0], emptyDict.Values);
            }
        }

        [Fact]
        public void TestEqualsAndHashCodeForPrimitives()
        {
            TypeBehavior.CheckEqualsAndHashCode(
                // each value in this list should be unequal to all the other values and equal to itself
                () => LdValue.Null,
                () => LdValue.Of(true),
                () => LdValue.Of(false),
                () => LdValue.Of(1),
                () => LdValue.Of(2),
                () => LdValue.Of("a"),
                () => LdValue.Of("b")
                );
        }
        
        [Fact]
        public void EqualsUsesDeepEqualityForArrays()
        {
            TypeBehavior.CheckEqualsAndHashCode(
                () => LdValue.BuildArray().Add("a")
                    .Add(LdValue.BuildArray().Add("b").Add("c").Build())
                    .Build(),
                () => LdValue.BuildArray().Add("a").Build(),
                () => LdValue.BuildArray().Add("a").Add("b").Add("c").Build(),
                () => LdValue.BuildArray().Add("a")
                    .Add(LdValue.BuildArray().Add("b").Add("x").Build())
                    .Build()
                );
        }

        [Fact]
        public void EqualsUsesDeepEqualityForObjects()
        {
            TypeBehavior.CheckEqualsAndHashCode(
                () => LdValue.BuildObject()
                    .Add("a", "b")
                    .Add("c", LdValue.BuildObject().Add("d", "e").Build())
                    .Build(),
                () => LdValue.BuildObject()
                    .Add("a", "b")
                    .Build(),
                () => LdValue.BuildObject()
                    .Add("a", "b")
                    .Add("c", LdValue.BuildObject().Add("d", "e").Build())
                    .Add("f", "g")
                    .Build(),
                () => LdValue.BuildObject()
                    .Add("a", "b")
                    .Add("c", LdValue.BuildObject().Add("d", "f").Build())
                    .Build()
                );
        }

        [Fact]
        public void ObjectBuilderAddDoesNotAllowDuplicates()
        {
            var builder = LdValue.BuildObject().Add("a", 1).Add("b", 2);
            Assert.ThrowsAny<Exception>(() => builder.Add("a", 3));
        }

        [Fact]
        public void ObjectBuilderSetAllowsDuplicates()
        {
            var builder = LdValue.BuildObject().Add("a", 1).Add("b", 2);
            builder.Set("a", 3);
            Assert.Equal(LdValue.BuildObject().Add("a", 3).Add("b", 2).Build(), builder.Build());
        }

        [Fact]
        public void ObjectBuilderRemove()
        {
            var builder = LdValue.BuildObject().Add("a", 1).Add("b", 2);
            builder.Remove("a");
            builder.Remove("c"); // nonexistent key is no-op
            Assert.Equal(LdValue.BuildObject().Add("b", 2).Build(), builder.Build());
        }

        [Fact]
        public void ObjectBuilderCanCopyProperties()
        {
            var builder = LdValue.BuildObject().Add("a", 0)
                .Copy(LdValue.BuildObject().Add("a", 1).Add("b", 2).Build())
                .Copy(LdValue.Of("ignore this"));
            Assert.Equal(LdValue.BuildObject().Add("a", 1).Add("b", 2).Build(), builder.Build());
        }

        [Fact]
        public void CanUseLongTypeForNumberGreaterThanMaxInt()
        {
            long n = (long)int.MaxValue + 1;
            Assert.Equal(n, LdValue.Of(n).AsLong);
            Assert.Equal(n, LdValue.Convert.Long.ToType(LdValue.Of(n)));
            Assert.Equal(n, LdValue.Convert.Long.FromType(n).AsLong);
        }

        [Fact]
        public void CanUseDoubleTypeForNumberGreaterThanMaxFloat()
        {
            double n = (double)float.MaxValue + 1;
            Assert.Equal(n, LdValue.Of(n).AsDouble);
            Assert.Equal(n, LdValue.Convert.Double.ToType(LdValue.Of(n)));
            Assert.Equal(n, LdValue.Convert.Double.FromType(n).AsDouble);
        }

        [Fact]
        public void TestJsonSerialization()
        {
            VerifySerializeAndParse(LdValue.Null, "null");
            VerifySerializeAndParse(aTrueBoolValue, "true");
            VerifySerializeAndParse(LdValue.Of(false), "false");
            VerifySerializeAndParse(anIntValue, someInt.ToString());
            VerifySerializeAndParse(aFloatValue, someFloat.ToString());
            VerifySerializeAndParse(anArrayValue, "[3]");
            VerifySerializeAndParse(anObjectValue, "{\"1\":\"x\"}");
            Assert.Throws<JsonException>(() => LdJsonSerialization.DeserializeObject<LdValue>("nono"));
            Assert.Throws<JsonException>(() => LdValue.Parse("nono"));
        }
        
        private void VerifySerializeAndParse(LdValue value, string expectedJson)
        {
            var json1 = LdJsonSerialization.SerializeObject(value);
            var json2 = value.ToJsonString();
            Assert.Equal(expectedJson, json1);
            Assert.Equal(json1, json2);
            var parsed1 = LdJsonSerialization.DeserializeObject<LdValue>(expectedJson);
            var parsed2 = LdValue.Parse(expectedJson);
            Assert.Equal(value, parsed1);
            Assert.Equal(value, parsed2);
        }
        
        [Fact]
        public void TestNullStringConstructorIsEquivalentToNullInstance()
        {
            Assert.Equal(LdValue.Null, LdValue.Of(null));
        }

        private static Dictionary<string, T> MakeDictionary<T>(params T[] values)
        {
            var ret = new Dictionary<string, T>();
            var i = 0;
            foreach (var v in values)
            {
                ret[(++i).ToString()] = v;
            }
            return ret;
        }

        private void AssertDictsEqual<T>(IReadOnlyDictionary<string, T> expected, IReadOnlyDictionary<string, T> actual)
        {
            if (expected.Count != actual.Count || expected.Except(actual).Any())
            {
                Assert.False(true, string.Format("expected: {0}, actual: {0}",
                    string.Join(", ", expected.Select(e => e.Key + "=" + e.Value)),
                    string.Join(", ", actual.Select(e => e.Key + "=" + e.Value))));
            }
        }

    }
}
