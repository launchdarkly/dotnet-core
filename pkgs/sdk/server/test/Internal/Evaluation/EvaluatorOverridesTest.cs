using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.Sdk.Server.Internal.Model;
using Xunit;

using static LaunchDarkly.Sdk.Server.Internal.Evaluation.EvaluatorTestUtil;
using static LaunchDarkly.Sdk.Server.Internal.Evaluation.EvaluatorTypes;

namespace LaunchDarkly.Sdk.Server.Internal.Evaluation
{
    // Tests of the override-affected marking. An evaluation is marked when any definition it read
    // carried the override marker: the evaluated flag, a prerequisite at any depth, or a segment
    // consulted during matching. The marking propagates upward only.
    public class EvaluatorOverridesTest
    {
        private static readonly Context context = Context.New("userkey");
        private static readonly LdValue offValue = LdValue.Of("off");
        private static readonly LdValue onValue = LdValue.Of("on");

        // Builds a flag that is on and serves variation 1 ("on") by fallthrough, with optional
        // prerequisites that must each serve variation 1.
        private static FeatureFlagBuilder OverrideTestFlag(string key, params string[] prereqKeys) =>
            new FeatureFlagBuilder(key).On(true).FallthroughVariation(1).OffVariation(0)
                .Variations(offValue, onValue)
                .Prerequisites(prereqKeys.Select(k => new Prerequisite(k, 1)).ToArray());

        private static void AssertOverrideAffected(bool expected, EvaluationDetail<LdValue> detail) =>
            Assert.Equal(expected, detail.Reason.OverrideAffected);

        private static PrerequisiteEvalRecord RequirePrereqRecord(EvalResult result, string prereqKey)
        {
            var records = result.PrerequisiteEvals.Where(r => r.PrerequisiteFlag.Key == prereqKey).ToList();
            Assert.True(records.Count == 1, "expected exactly one record for prerequisite " + prereqKey);
            return records[0];
        }

        [Fact]
        public void OverrideFlagMarksOffEvaluation()
        {
            var flag = new FeatureFlagBuilder("feature").On(false).OffVariation(0).Variations(offValue, onValue)
                .Build().AsOverride();

            var result = BasicEvaluator.Evaluate(flag, context);

            Assert.Equal(EvaluationReasonKind.Off, result.Result.Reason.Kind);
            Assert.Equal(offValue, result.Result.Value);
            AssertOverrideAffected(true, result.Result);
        }

        [Fact]
        public void OverrideFlagMarksFallthroughEvaluation()
        {
            var flag = OverrideTestFlag("feature").Build().AsOverride();

            var result = BasicEvaluator.Evaluate(flag, context);

            Assert.Equal(EvaluationReasonKind.Fallthrough, result.Result.Reason.Kind);
            Assert.Equal(onValue, result.Result.Value);
            AssertOverrideAffected(true, result.Result);
        }

        [Fact]
        public void OverrideFlagMarksRuleMatchEvaluation()
        {
            var flag = new FeatureFlagBuilder("feature").On(true).FallthroughVariation(0).OffVariation(0)
                .Variations(offValue, onValue)
                .Rules(new RuleBuilder().Id("rule-id").Variation(1).Clauses(ClauseBuilder.ShouldMatchUser(context)).Build())
                .Build().AsOverride();

            var result = BasicEvaluator.Evaluate(flag, context);

            Assert.Equal(EvaluationReasonKind.RuleMatch, result.Result.Reason.Kind);
            Assert.Equal("rule-id", result.Result.Reason.RuleId);
            Assert.Equal(onValue, result.Result.Value);
            AssertOverrideAffected(true, result.Result);
        }

        [Fact]
        public void PlainFlagIsNotMarked()
        {
            var flag = new FeatureFlagBuilder("feature").On(false).OffVariation(0).Variations(offValue, onValue).Build();

            var result = BasicEvaluator.Evaluate(flag, context);

            Assert.Equal(EvaluationReasonKind.Off, result.Result.Reason.Kind);
            AssertOverrideAffected(false, result.Result);
        }

        [Fact]
        public void PlainFlagWithPlainPrerequisiteAndSegmentIsNotMarked()
        {
            var segment = new SegmentBuilder("segment").Included(context.Key).Build();
            var prereq = new FeatureFlagBuilder("prereq").BooleanMatchingSegment(segment.Key).Build();
            var flag = OverrideTestFlag("feature", "prereq").Build();
            var evaluator = BasicEvaluator.WithStoredFlags(prereq).WithStoredSegments(segment);

            var result = evaluator.Evaluate(flag, context);

            Assert.Equal(EvaluationReasonKind.Fallthrough, result.Result.Reason.Kind);
            Assert.Equal(onValue, result.Result.Value);
            AssertOverrideAffected(false, result.Result);
            AssertOverrideAffected(false, RequirePrereqRecord(result, "prereq").Result);
        }

        [Fact]
        public void MalformedOverrideFlagErrorResultIsMarked()
        {
            var flag = new FeatureFlagBuilder("feature").On(true).FallthroughVariation(99).Variations(offValue, onValue)
                .Build().AsOverride();

            var result = BasicEvaluator.Evaluate(flag, context);

            Assert.Equal(EvaluationReasonKind.Error, result.Result.Reason.Kind);
            Assert.Equal(EvaluationErrorKind.MalformedFlag, result.Result.Reason.ErrorKind);
            Assert.Equal(LdValue.Null, result.Result.Value);
            AssertOverrideAffected(true, result.Result);
        }

        [Fact]
        public void PrerequisiteCycleThroughOverrideFlagIsMarked()
        {
            // feature -> prereq -> feature; only prereq is an override
            var prereq = OverrideTestFlag("prereq", "feature").Build().AsOverride();
            var flag = OverrideTestFlag("feature", "prereq").Build();
            var evaluator = BasicEvaluator.WithStoredFlags(flag, prereq);

            var result = evaluator.Evaluate(flag, context);

            Assert.Equal(EvaluationReasonKind.Error, result.Result.Reason.Kind);
            Assert.Equal(EvaluationErrorKind.MalformedFlag, result.Result.Reason.ErrorKind);
            AssertOverrideAffected(true, result.Result);
            Assert.Empty(result.PrerequisiteEvals);
        }

        [Fact]
        public void PrerequisiteCycleWithoutOverrideIsNotMarked()
        {
            var prereq = OverrideTestFlag("prereq", "feature").Build();
            var flag = OverrideTestFlag("feature", "prereq").Build();
            var evaluator = BasicEvaluator.WithStoredFlags(flag, prereq);

            var result = evaluator.Evaluate(flag, context);

            Assert.Equal(EvaluationErrorKind.MalformedFlag, result.Result.Reason.ErrorKind);
            AssertOverrideAffected(false, result.Result);
        }

        [Fact]
        public void InvalidContextErrorResultOfOverrideFlagIsMarked()
        {
            var flag = OverrideTestFlag("feature").Build().AsOverride();

            var result = BasicEvaluator.Evaluate(flag, Context.New(""));

            Assert.Equal(EvaluationReasonKind.Error, result.Result.Reason.Kind);
            Assert.Equal(EvaluationErrorKind.UserNotSpecified, result.Result.Reason.ErrorKind);
            AssertOverrideAffected(true, result.Result);
        }

        [Fact]
        public void UnexpectedExceptionResultOfOverrideFlagIsMarked()
        {
            var flag = OverrideTestFlag("feature", "prereq").Build().AsOverride();
            // The basic evaluator throws when asked for any flag it does not know about.
            var result = BasicEvaluator.Evaluate(flag, context);

            Assert.Equal(EvaluationErrorKind.Exception, result.Result.Reason.ErrorKind);
            AssertOverrideAffected(true, result.Result);
        }

        [Fact]
        public void OverridePrerequisiteMarksPrerequisiteRecordAndTopLevel()
        {
            var prereq = OverrideTestFlag("prereq").Build().AsOverride();
            var flag = OverrideTestFlag("feature", "prereq").Build();
            var evaluator = BasicEvaluator.WithStoredFlags(prereq);

            var result = evaluator.Evaluate(flag, context);

            Assert.Equal(EvaluationReasonKind.Fallthrough, result.Result.Reason.Kind);
            AssertOverrideAffected(true, result.Result);

            var record = RequirePrereqRecord(result, "prereq");
            Assert.Equal(EvaluationReasonKind.Fallthrough, record.Result.Reason.Kind);
            Assert.Equal("feature", record.FlagKey);
            AssertOverrideAffected(true, record.Result);
        }

        [Fact]
        public void OverrideFlagDoesNotMarkUnaffectedPrerequisiteRecord()
        {
            var prereq = OverrideTestFlag("prereq").Build();
            var flag = OverrideTestFlag("feature", "prereq").Build().AsOverride();
            var evaluator = BasicEvaluator.WithStoredFlags(prereq);

            var result = evaluator.Evaluate(flag, context);

            AssertOverrideAffected(true, result.Result);
            AssertOverrideAffected(false, RequirePrereqRecord(result, "prereq").Result);
        }

        [Fact]
        public void OverridePrerequisiteAtDepthTwoMarksAllAffectedScopes()
        {
            var prereq2 = OverrideTestFlag("prereq2").Build().AsOverride();
            var prereq1 = OverrideTestFlag("prereq1", "prereq2").Build();
            var flag = OverrideTestFlag("feature", "prereq1").Build();
            var evaluator = BasicEvaluator.WithStoredFlags(prereq1, prereq2);

            var result = evaluator.Evaluate(flag, context);

            Assert.Equal(EvaluationReasonKind.Fallthrough, result.Result.Reason.Kind);
            AssertOverrideAffected(true, result.Result);

            // The nested record is produced first, during the evaluation of prereq1.
            Assert.Equal(2, result.PrerequisiteEvals.Count);
            Assert.Equal("prereq2", result.PrerequisiteEvals[0].PrerequisiteFlag.Key);
            AssertOverrideAffected(true, result.PrerequisiteEvals[0].Result);
            Assert.Equal("prereq1", result.PrerequisiteEvals[1].PrerequisiteFlag.Key);
            AssertOverrideAffected(true, result.PrerequisiteEvals[1].Result);
        }

        [Fact]
        public void UnaffectedSiblingPrerequisiteRecordStaysUnmarked()
        {
            // Flag a has prerequisites b and c. Only d, a prerequisite of b, is an override. The
            // marking reaches a, b, and d. It does not reach the sibling c, and the plain segments s1
            // and s2 mark nothing.
            var s1 = new SegmentBuilder("s1").Included(context.Key).Build();
            var s2 = new SegmentBuilder("s2").Included(context.Key).Build();
            var d = OverrideTestFlag("d").Build().AsOverride();
            var b = OverrideTestFlag("b", "d").Build();
            var c = new FeatureFlagBuilder("c").BooleanMatchingSegment(s1.Key).Build();
            var a = new FeatureFlagBuilder("a").On(true).FallthroughVariation(0).OffVariation(0)
                .Prerequisites(new Prerequisite("b", 1), new Prerequisite("c", 1))
                .Rules(new RuleBuilder().Id("rule-s2").Variation(1).Clauses(ClauseBuilder.ShouldMatchSegment(s2.Key)).Build())
                .Variations(offValue, onValue)
                .Build();
            var evaluator = BasicEvaluator.WithStoredFlags(b, c, d).WithStoredSegments(s1, s2);

            var result = evaluator.Evaluate(a, context);

            Assert.Equal(EvaluationReasonKind.RuleMatch, result.Result.Reason.Kind);
            Assert.Equal(onValue, result.Result.Value);
            AssertOverrideAffected(true, result.Result);

            Assert.Equal(3, result.PrerequisiteEvals.Count);
            AssertOverrideAffected(true, RequirePrereqRecord(result, "d").Result);
            AssertOverrideAffected(true, RequirePrereqRecord(result, "b").Result);
            AssertOverrideAffected(false, RequirePrereqRecord(result, "c").Result);
        }

        [Fact]
        public void OverrideSegmentReferencedByFlagRuleMarksEvaluation()
        {
            var segment = new SegmentBuilder("segment").Included(context.Key).Build().AsOverride();
            var flag = new FeatureFlagBuilder("feature").BooleanMatchingSegment(segment.Key).Build();
            var evaluator = BasicEvaluator.WithStoredSegments(segment);

            var result = evaluator.Evaluate(flag, context);

            Assert.Equal(EvaluationReasonKind.RuleMatch, result.Result.Reason.Kind);
            Assert.Equal(LdValue.Of(true), result.Result.Value);
            AssertOverrideAffected(true, result.Result);
        }

        [Fact]
        public void OverrideSegmentReadWithoutMatchingMarksEvaluation()
        {
            var segment = new SegmentBuilder("segment").Included("someone-else").Build().AsOverride();
            var flag = new FeatureFlagBuilder("feature").BooleanMatchingSegment(segment.Key).Build();
            var evaluator = BasicEvaluator.WithStoredSegments(segment);

            var result = evaluator.Evaluate(flag, context);

            Assert.Equal(EvaluationReasonKind.Fallthrough, result.Result.Reason.Kind);
            Assert.Equal(LdValue.Of(false), result.Result.Value);
            AssertOverrideAffected(true, result.Result);
        }

        [Fact]
        public void OverrideSegmentReadByNegatedClauseMarksEvaluation()
        {
            var segment = new SegmentBuilder("segment").Included("someone-else").Build().AsOverride();
            var flag = new FeatureFlagBuilder("feature")
                .BooleanWithClauses(ClauseBuilder.ShouldNotMatchSegment(segment.Key)).Build();
            var evaluator = BasicEvaluator.WithStoredSegments(segment);

            var result = evaluator.Evaluate(flag, context);

            Assert.Equal(EvaluationReasonKind.RuleMatch, result.Result.Reason.Kind);
            Assert.Equal(LdValue.Of(true), result.Result.Value);
            AssertOverrideAffected(true, result.Result);
        }

        [Fact]
        public void OverrideSegmentReferencedBySegmentRuleMarksEvaluation()
        {
            // The outer segment is plain. A rule of the outer segment reads a nested override segment.
            var nested = new SegmentBuilder("nested-segment").Included(context.Key).Build().AsOverride();
            var outer = new SegmentBuilder("outer-segment")
                .Rules(new SegmentRuleBuilder().Clauses(ClauseBuilder.ShouldMatchSegment(nested.Key)).Build())
                .Build();
            var flag = new FeatureFlagBuilder("feature").BooleanMatchingSegment(outer.Key).Build();
            var evaluator = BasicEvaluator.WithStoredSegments(outer, nested);

            var result = evaluator.Evaluate(flag, context);

            Assert.Equal(EvaluationReasonKind.RuleMatch, result.Result.Reason.Kind);
            Assert.Equal(LdValue.Of(true), result.Result.Value);
            AssertOverrideAffected(true, result.Result);
        }

        [Fact]
        public void OverrideSegmentReferencedByPrerequisiteMarksPrerequisiteRecordAndTopLevel()
        {
            var segment = new SegmentBuilder("segment").Included(context.Key).Build().AsOverride();
            var prereq = new FeatureFlagBuilder("prereq").BooleanMatchingSegment(segment.Key).Build();
            var flag = OverrideTestFlag("feature", "prereq").Build();
            var evaluator = BasicEvaluator.WithStoredFlags(prereq).WithStoredSegments(segment);

            var result = evaluator.Evaluate(flag, context);

            Assert.Equal(EvaluationReasonKind.Fallthrough, result.Result.Reason.Kind);
            AssertOverrideAffected(true, result.Result);
            AssertOverrideAffected(true, RequirePrereqRecord(result, "prereq").Result);
        }

        [Fact]
        public void MissingPrerequisiteDoesNotMarkEvaluation()
        {
            // The store holds an unrelated override definition to show that only reads count.
            var unrelated = OverrideTestFlag("unrelated").Build().AsOverride();
            var flag = OverrideTestFlag("feature", "missing").Build();
            var evaluator = BasicEvaluator.WithStoredFlags(unrelated).WithNonexistentFlag("missing");

            var result = evaluator.Evaluate(flag, context);

            Assert.Equal(EvaluationReason.PrerequisiteFailedReason("missing"), result.Result.Reason);
            Assert.Equal(offValue, result.Result.Value);
            AssertOverrideAffected(false, result.Result);
            Assert.Empty(result.PrerequisiteEvals);
        }

        [Fact]
        public void MissingSegmentDoesNotMarkEvaluation()
        {
            var unrelated = new SegmentBuilder("unrelated").Included(context.Key).Build().AsOverride();
            var flag = new FeatureFlagBuilder("feature").BooleanMatchingSegment("missing").Build();
            var evaluator = BasicEvaluator.WithStoredSegments(unrelated).WithNonexistentSegment("missing");

            var result = evaluator.Evaluate(flag, context);

            Assert.Equal(EvaluationReasonKind.Fallthrough, result.Result.Reason.Kind);
            Assert.Equal(LdValue.Of(false), result.Result.Value);
            AssertOverrideAffected(false, result.Result);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void TopLevelAndPrerequisiteRecordsReportTheirOwnMarking(bool flagOverride, bool prereqOverride)
        {
            var prereq = OverrideTestFlag("prereq").Build();
            if (prereqOverride)
            {
                prereq = prereq.AsOverride();
            }
            var flag = OverrideTestFlag("feature", "prereq").Build();
            if (flagOverride)
            {
                flag = flag.AsOverride();
            }
            var evaluator = BasicEvaluator.WithStoredFlags(prereq);

            var result = evaluator.Evaluate(flag, context);

            AssertOverrideAffected(flagOverride || prereqOverride, result.Result);
            AssertOverrideAffected(prereqOverride, RequirePrereqRecord(result, "prereq").Result);
        }

        [Fact]
        public void MarkingIsCombinedWithBigSegmentsStatus()
        {
            var segment = new SegmentBuilder("segment").Unbounded(true).Generation(1).Build().AsOverride();
            var flag = new FeatureFlagBuilder("feature").BooleanMatchingSegment(segment.Key).Build();
            var bigSegments = new MockBigSegmentProvider { Status = BigSegmentsStatus.Stale };
            var evaluator = BasicEvaluator.WithStoredSegments(segment).WithBigSegments(bigSegments);

            var result = evaluator.Evaluate(flag, context);

            Assert.Equal(BigSegmentsStatus.Stale, result.Result.Reason.BigSegmentsStatus);
            AssertOverrideAffected(true, result.Result);
        }
    }
}
