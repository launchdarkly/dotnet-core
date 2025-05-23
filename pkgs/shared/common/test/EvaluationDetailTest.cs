using System.Collections.Generic;
using LaunchDarkly.Sdk.Json;
using LaunchDarkly.TestHelpers;
using Xunit;

namespace LaunchDarkly.Sdk
{
    public class EvaluationDetailTest
    {
        [Fact]
        public void TestIsDefaultValueTrue()
        {
            var detail = new EvaluationDetail<string>("default", null, EvaluationReason.OffReason);
            Assert.True(detail.IsDefaultValue);
        }

        [Fact]
        public void TestIsDefaultValueFalse()
        {
            var detail = new EvaluationDetail<string>("default", 0, EvaluationReason.OffReason);
            Assert.False(detail.IsDefaultValue);
        }

        public struct ReasonTestCase
        {
            public EvaluationReason Reason { get; set; }
            public string JsonString { get; set; }
            public string ExpectedShortString { get; set; }
        }

        [Fact]
        public void TestReasonSerializationDeserialization()
        {
            foreach (var test in new ReasonTestCase[]
            {
                new ReasonTestCase { Reason = EvaluationReason.OffReason,
                    JsonString = @"{""kind"":""OFF""}", ExpectedShortString = "OFF" },
                new ReasonTestCase { Reason = EvaluationReason.FallthroughReason,
                    JsonString = @"{""kind"":""FALLTHROUGH""}", ExpectedShortString = "FALLTHROUGH" },
                new ReasonTestCase {
                    Reason = EvaluationReason.FallthroughReason.WithInExperiment(true),
                    JsonString = @"{""kind"":""FALLTHROUGH"",""inExperiment"":true}",
                    ExpectedShortString = "FALLTHROUGH" },
                new ReasonTestCase { Reason = EvaluationReason.FallthroughReason.WithBigSegmentsStatus(BigSegmentsStatus.Healthy),
                    JsonString = @"{""kind"":""FALLTHROUGH"",""bigSegmentsStatus"":""HEALTHY""}", ExpectedShortString = "FALLTHROUGH" },
                new ReasonTestCase {
                    Reason = EvaluationReason.FallthroughReason.WithInExperiment(true).WithBigSegmentsStatus(BigSegmentsStatus.Healthy),
                    JsonString = @"{""kind"":""FALLTHROUGH"",""inExperiment"":true,""bigSegmentsStatus"":""HEALTHY""}",
                    ExpectedShortString = "FALLTHROUGH" },
                new ReasonTestCase { Reason = EvaluationReason.TargetMatchReason,
                    JsonString = @"{""kind"":""TARGET_MATCH""}", ExpectedShortString = "TARGET_MATCH" },
                new ReasonTestCase { Reason = EvaluationReason.RuleMatchReason(1, "id"),
                    JsonString = @"{""kind"":""RULE_MATCH"",""ruleIndex"":1,""ruleId"":""id""}",
                    ExpectedShortString = "RULE_MATCH(1,id)"
                },
                new ReasonTestCase { Reason = EvaluationReason.RuleMatchReason(1, "id").WithInExperiment(true),
                    JsonString = @"{""kind"":""RULE_MATCH"",""ruleIndex"":1,""ruleId"":""id"",""inExperiment"":true}",
                    ExpectedShortString = "RULE_MATCH(1,id)"
                },
                new ReasonTestCase { Reason = EvaluationReason.RuleMatchReason(1, "id").WithBigSegmentsStatus(BigSegmentsStatus.Healthy),
                    JsonString = @"{""kind"":""RULE_MATCH"",""ruleIndex"":1,""ruleId"":""id"",""bigSegmentsStatus"":""HEALTHY""}",
                    ExpectedShortString = "RULE_MATCH(1,id)"
                },
                new ReasonTestCase { Reason = EvaluationReason.RuleMatchReason(1, "id").WithInExperiment(true).WithBigSegmentsStatus(BigSegmentsStatus.Healthy),
                    JsonString = @"{""kind"":""RULE_MATCH"",""ruleIndex"":1,""ruleId"":""id"",""inExperiment"":true,""bigSegmentsStatus"":""HEALTHY""}",
                    ExpectedShortString = "RULE_MATCH(1,id)"
                },
                new ReasonTestCase { Reason = EvaluationReason.PrerequisiteFailedReason("key"),
                    JsonString = @"{""kind"":""PREREQUISITE_FAILED"",""prerequisiteKey"":""key""}",
                    ExpectedShortString = "PREREQUISITE_FAILED(key)"
                },
                new ReasonTestCase { Reason = EvaluationReason.ErrorReason(EvaluationErrorKind.Exception),
                    JsonString = @"{""kind"":""ERROR"",""errorKind"":""EXCEPTION""}",
                    ExpectedShortString = "ERROR(EXCEPTION)"
                }
            })
            {
                AssertJsonEqual(test.JsonString, LdJsonSerialization.SerializeObject(test.Reason));
                Assert.Equal(test.Reason, LdJsonSerialization.DeserializeObject<EvaluationReason>(test.JsonString));
                Assert.Equal(test.ExpectedShortString, test.Reason.ToString());
            }
        }

        [Fact]
        public void TestBigSegmentsStatusSerializationDeserialization()
        {
            foreach (var test in new KeyValuePair<BigSegmentsStatus, string>[]
            {
                new KeyValuePair<BigSegmentsStatus, string>(BigSegmentsStatus.Healthy, "HEALTHY"),
                new KeyValuePair<BigSegmentsStatus, string>(BigSegmentsStatus.Stale, "STALE"),
                new KeyValuePair<BigSegmentsStatus, string>(BigSegmentsStatus.NotConfigured, "NOT_CONFIGURED"),
                new KeyValuePair<BigSegmentsStatus, string>(BigSegmentsStatus.StoreError, "STORE_ERROR"),
            })
            {
                var reason = EvaluationReason.FallthroughReason.WithBigSegmentsStatus(test.Key);
                var reasonJson = LdJsonSerialization.SerializeObject(reason);
                Assert.Equal(LdValue.Parse(reasonJson).Get("bigSegmentsStatus"), LdValue.Of(test.Value));
                var reason1 = LdJsonSerialization.DeserializeObject<EvaluationReason>(reasonJson);
                Assert.Equal(test.Key, reason1.BigSegmentsStatus);
            }
        }

        [Fact]
        public void TestErrorKindSerializationDeserialization()
        {
            foreach (var test in new KeyValuePair<EvaluationErrorKind, string>[]
            {
                new KeyValuePair<EvaluationErrorKind, string>(EvaluationErrorKind.ClientNotReady, "CLIENT_NOT_READY"),
                new KeyValuePair<EvaluationErrorKind, string>(EvaluationErrorKind.Exception, "EXCEPTION"),
                new KeyValuePair<EvaluationErrorKind, string>(EvaluationErrorKind.FlagNotFound, "FLAG_NOT_FOUND"),
                new KeyValuePair<EvaluationErrorKind, string>(EvaluationErrorKind.MalformedFlag, "MALFORMED_FLAG"),
                new KeyValuePair<EvaluationErrorKind, string>(EvaluationErrorKind.UserNotSpecified, "USER_NOT_SPECIFIED"),
                new KeyValuePair<EvaluationErrorKind, string>(EvaluationErrorKind.WrongType, "WRONG_TYPE"),
            })
            {
                var reason = EvaluationReason.ErrorReason(test.Key);
                var reasonJson = LdJsonSerialization.SerializeObject(reason);
                Assert.Equal(LdValue.Parse(reasonJson).Get("errorKind"), LdValue.Of(test.Value));
                var reason1 = LdJsonSerialization.DeserializeObject<EvaluationReason>(reasonJson);
                Assert.Equal(test.Key, reason1.ErrorKind);
            }
        }

        [Fact]
        public void TestEqualityAndHashCode()
        {
            TypeBehavior.CheckEqualsAndHashCode(
                // each value in this list should be unequal to all the other values and equal to itself
                () => EvaluationReason.OffReason,
                () => EvaluationReason.FallthroughReason,
                () => EvaluationReason.FallthroughReason.WithInExperiment(true),
                () => EvaluationReason.RuleMatchReason(0, "rule1"),
                () => EvaluationReason.RuleMatchReason(0, "rule1").WithInExperiment(true),
                () => EvaluationReason.RuleMatchReason(0, "rule1").WithBigSegmentsStatus(BigSegmentsStatus.Stale),
                () => EvaluationReason.RuleMatchReason(1, "rule2"),
                () => EvaluationReason.PrerequisiteFailedReason("a"),
                () => EvaluationReason.PrerequisiteFailedReason("b"),
                () => EvaluationReason.ErrorReason(EvaluationErrorKind.FlagNotFound),
                () => EvaluationReason.ErrorReason(EvaluationErrorKind.Exception)
                );
        }

        private void AssertJsonEqual(string expectedString, string actualString)
        {
            Assert.Equal(LdValue.Parse(expectedString), LdValue.Parse(actualString));
        }
    }
}
