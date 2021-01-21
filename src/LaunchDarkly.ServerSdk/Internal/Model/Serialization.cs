﻿using System.Collections.Generic;
using System.Collections.Immutable;
using LaunchDarkly.JsonStream;
using LaunchDarkly.Sdk.Json;

namespace LaunchDarkly.Sdk.Server.Internal.Model
{
    internal class FeatureFlagSerialization : IJsonStreamConverter
    {
        internal static readonly FeatureFlagSerialization Instance = new FeatureFlagSerialization();
        internal static readonly string[] _requiredProperties = new string[] { "version" };

        public void WriteJson(object value, IValueWriter writer)
        {
            var flag = (FeatureFlag)value;
            var obj = writer.Object();

            obj.Name("key").String(flag.Key);
            obj.Name("version").Int(flag.Version);
            obj.Name("deleted").Bool(flag.Deleted);
            obj.Name("on").Bool(flag.On);

            var prereqsArr = obj.Name("prerequisites").Array();
            foreach (var p in flag.Prerequisites)
            {
                var prereqObj = prereqsArr.Object();
                prereqObj.Name("key").String(p.Key);
                prereqObj.Name("variation").Int(p.Variation);
                prereqObj.End();
            }
            prereqsArr.End();

            var targetsArr = obj.Name("targets").Array();
            foreach (var t in flag.Targets)
            {
                var targetObj = targetsArr.Object();
                targetObj.Name("variation").Int(t.Variation);
                SerializationHelpers.WriteStrings(targetObj.Name("values"), t.Values);
                targetObj.End();
            }
            targetsArr.End();

            var rulesArr = obj.Name("rules").Array();
            foreach (var r in flag.Rules)
            {
                var ruleObj = rulesArr.Object();
                ruleObj.Name("id").String(r.Id);
                SerializationHelpers.WriteVariationOrRollout(ref ruleObj, r.Variation, r.Rollout);
                SerializationHelpers.WriteClauses(ruleObj.Name("clauses"), r.Clauses);
                ruleObj.Name("trackEvents").Bool(r.TrackEvents);
                ruleObj.End();
            }
            rulesArr.End();

            var fallthroughObj = obj.Name("fallthrough").Object();
            SerializationHelpers.WriteVariationOrRollout(ref fallthroughObj, flag.Fallthrough.Variation, flag.Fallthrough.Rollout);
            fallthroughObj.End();

            if (flag.OffVariation.HasValue)
            {
                obj.Name("offVariation").Int(flag.OffVariation.Value);
            }
            SerializationHelpers.WriteValues(obj.Name("variations"), flag.Variations);
            obj.Name("salt").String(flag.Salt);
            obj.MaybeName("trackEvents", flag.TrackEvents).Bool(flag.TrackEvents);
            obj.MaybeName("trackEventsFallthrough", flag.TrackEventsFallthrough).Bool(flag.TrackEventsFallthrough);
            if (flag.DebugEventsUntilDate.HasValue)
            {
                obj.Name("debugEventsUntilDate").Long(flag.DebugEventsUntilDate.Value.Value);
            }
            obj.Name("clientSide").Bool(flag.ClientSide);

            obj.End();
        }

        public object ReadJson(ref JReader reader)
        {
            string key = null;
            int version = 0;
            bool deleted = false;
            bool on = false;
            ImmutableList<Prerequisite> prerequisites = null;
            ImmutableList<Target> targets = null;
            ImmutableList<FlagRule> rules = null;
            string salt = null;
            VariationOrRollout fallthrough = new VariationOrRollout();
            int? offVariation = null;
            ImmutableList<LdValue> variations = null;
            bool trackEvents = false, trackEventsFallthrough = false;
            UnixMillisecondTime? debugEventsUntilDate = null;
            bool clientSide = false;

            for (var obj = reader.Object().WithRequiredProperties(_requiredProperties); obj.Next(ref reader);)
            {
                switch (obj.Name)
                {
                    case var n when n == "key":
                        key = reader.String();
                        break;
                    case var n when n == "version":
                        version = reader.Int();
                        break;
                    case var n when n == "deleted":
                        deleted = reader.Bool();
                        break;
                    case var n when n == "on":
                        on = reader.Bool();
                        break;
                    case var n when n == "prerequisites":
                        var prereqsBuilder = ImmutableList.CreateBuilder<Prerequisite>();
                        for (var arr = reader.ArrayOrNull(); arr.Next(ref reader);)
                        {
                            prereqsBuilder.Add(ReadPrerequisite(ref reader));
                        }
                        prerequisites = prereqsBuilder.ToImmutable();
                        break;
                    case var n when n == "targets":
                        var targetsBuilder = ImmutableList.CreateBuilder<Target>();
                        for (var arr = reader.ArrayOrNull(); arr.Next(ref reader);)
                        {
                            targetsBuilder.Add(ReadTarget(ref reader));
                        }
                        targets = targetsBuilder.ToImmutable();
                        break;
                    case var n when n == "rules":
                        var rulesBuilder = ImmutableList.CreateBuilder<FlagRule>();
                        for (var arr = reader.ArrayOrNull(); arr.Next(ref reader);)
                        {
                            rulesBuilder.Add(ReadFlagRule(ref reader));
                        }
                        rules = rulesBuilder.ToImmutable();
                        break;
                    case var n when n == "fallthrough":
                        fallthrough = ReadVariationOrRollout(ref reader);
                        break;
                    case var n when n == "offVariation":
                        offVariation = reader.IntOrNull();
                        break;
                    case var n when n == "variations":
                        variations = SerializationHelpers.ReadValues(ref reader);
                        break;
                    case var n when n == "salt":
                        salt = reader.StringOrNull();
                        break;
                    case var n when n == "trackEvents":
                        trackEvents = reader.Bool();
                        break;
                    case var n when n == "trackEventsFallthrough":
                        trackEventsFallthrough = reader.Bool();
                        break;
                    case var n when n == "debugEventsUntilDate":
                        var dt = reader.LongOrNull();
                        debugEventsUntilDate = dt.HasValue ? UnixMillisecondTime.OfMillis(dt.Value) : (UnixMillisecondTime?)null;
                        break;
                    case var n when n == "clientSide":
                        clientSide = reader.Bool();
                        break;
                }
            }
            if (key is null && !deleted)
            {
                throw new RequiredPropertyException("key", 0);
            }
            return new FeatureFlag(key, version, deleted, on, prerequisites, targets, rules, fallthrough,
                offVariation, variations, salt, trackEvents, trackEventsFallthrough, debugEventsUntilDate, clientSide);
        }

        internal static Prerequisite ReadPrerequisite(ref JReader r)
        {
            string key = null;
            int variation = 0;
            for (var obj = r.Object(); obj.Next(ref r);)
            {
                switch (obj.Name)
                {
                    case var n when n == "key":
                        key = r.String();
                        break;
                    case var n when n == "variation":
                        variation = r.Int();
                        break;
                }
            }
            return new Prerequisite(key, variation);
        }

        internal static Target ReadTarget(ref JReader r)
        {
            ImmutableList<string> values = null;
            int variation = 0;
            for (var obj = r.Object(); obj.Next(ref r);)
            {
                switch (obj.Name)
                {
                    case var n when n == "values":
                        values = SerializationHelpers.ReadStrings(ref r);
                        break;
                    case var n when n == "variation":
                        variation = r.Int();
                        break;
                }
            }
            return new Target(values, variation);
        }

        internal static FlagRule ReadFlagRule(ref JReader r)
        {
            string id = null;
            int? variation = null;
            Rollout? rollout = null;
            ImmutableList<Clause> clauses = null;
            bool trackEvents = false;
            for (var obj = r.Object(); obj.Next(ref r);)
            {
                switch (obj.Name)
                {
                    case var n when n == "id":
                        id = r.String();
                        break;
                    case var n when n == "variation":
                        variation = r.IntOrNull();
                        break;
                    case var n when n == "rollout":
                        rollout = ReadRollout(ref r);
                        break;
                    case var n when n == "clauses":
                        clauses = SerializationHelpers.ReadClauses(ref r);
                        break;
                    case var n when n == "trackEvents":
                        trackEvents = r.Bool();
                        break;
                }
            }
            return new FlagRule(variation, rollout, id, clauses, trackEvents);
        }

        internal static VariationOrRollout ReadVariationOrRollout(ref JReader r)
        {
            int? variation = null;
            Rollout? rollout = null;
            for (var obj = r.Object(); obj.Next(ref r);)
            {
                switch (obj.Name)
                {
                    case var n when n == "variation":
                        variation = r.IntOrNull();
                        break;
                    case var n when n == "rollout":
                        rollout = ReadRollout(ref r);
                        break;
                }
            }
            return new VariationOrRollout(variation, rollout);
        }

        internal static Rollout ReadRollout(ref JReader r)
        {
            ImmutableList<WeightedVariation> variations = null;
            UserAttribute? bucketBy = null;
            for (var obj = r.Object(); obj.Next(ref r);)
            {
                switch (obj.Name)
                {
                    case var n when n == "variations":
                        var listBuilder = ImmutableList.CreateBuilder<WeightedVariation>();
                        for (var arr = r.ArrayOrNull(); arr.Next(ref r);)
                        {
                            int variation = 0, weight = 0;
                            for (var wvObj = r.Object(); wvObj.Next(ref r);)
                            {
                                switch (wvObj.Name)
                                {
                                    case var nn when nn == "variation":
                                        variation = r.Int();
                                        break;
                                    case var nn when nn == "weight":
                                        weight = r.Int();
                                        break;
                                }
                            }
                            listBuilder.Add(new WeightedVariation(variation, weight));
                        }
                        variations = listBuilder.ToImmutable();
                        break;
                    case var n when n == "bucketBy":
                        var s = r.StringOrNull();
                        bucketBy = s is null ? (UserAttribute?)null : UserAttribute.ForName(s);
                        break;
                }
            }
            return new Rollout(variations, bucketBy);
        }
    }

    internal class SegmentSerialization : IJsonStreamConverter
    {
        internal static readonly SegmentSerialization Instance = new SegmentSerialization();
        internal static readonly string[] _requiredProperties = new string[] { "version" };

        public void WriteJson(object value, IValueWriter writer)
        {
            var segment = (Segment)value;
            var obj = writer.Object();

            obj.Name("key").String(segment.Key);
            obj.Name("version").Int(segment.Version);
            obj.Name("deleted").Bool(segment.Deleted);
            SerializationHelpers.WriteStrings(obj.Name("included"), segment.Included);
            SerializationHelpers.WriteStrings(obj.Name("excluded"), segment.Excluded);
            obj.Name("salt").String(segment.Salt);

            var rulesArr = obj.Name("rules").Array();
            foreach (var r in segment.Rules)
            {
                var ruleObj = rulesArr.Object();
                SerializationHelpers.WriteClauses(ruleObj.Name("clauses"), r.Clauses);
                if (r.Weight.HasValue)
                {
                    ruleObj.Name("weight").Int(r.Weight.Value);
                }
                if (r.BucketBy.HasValue)
                {
                    ruleObj.Name("bucketBy").String(r.BucketBy.Value.AttributeName);
                }
                ruleObj.End();
            }
            rulesArr.End();

            obj.End();
        }

        public object ReadJson(ref JReader reader)
        {
            string key = null;
            int version = 0;
            bool deleted = false;
            ImmutableList<string> included = null, excluded = null;
            ImmutableList<SegmentRule> rules = null;
            string salt = null;

            for (var obj = reader.Object().WithRequiredProperties(_requiredProperties); obj.Next(ref reader);)
            {
                switch (obj.Name)
                {
                    case var n when n == "key":
                        key = reader.String();
                        break;
                    case var n when n == "version":
                        version = reader.Int();
                        break;
                    case var n when n == "deleted":
                        deleted = reader.Bool();
                        break;
                    case var n when n == "included":
                        included = SerializationHelpers.ReadStrings(ref reader);
                        break;
                    case var n when n == "excluded":
                        excluded = SerializationHelpers.ReadStrings(ref reader);
                        break;
                    case var n when n == "rules":
                        var rulesBuilder = ImmutableList.CreateBuilder<SegmentRule>();
                        for (var rulesArr = reader.Array(); rulesArr.Next(ref reader);)
                        {
                            rulesBuilder.Add(ReadSegmentRule(ref reader));
                        }
                        rules = rulesBuilder.ToImmutable();
                        break;
                    case var n when n == "salt":
                        salt = reader.StringOrNull();
                        break;
                }
            }
            if (key is null && !deleted)
            {
                throw new RequiredPropertyException("key", 0);
            }
            return new Segment(key, version, deleted, included, excluded, rules, salt);
        }

        internal static SegmentRule ReadSegmentRule(ref JReader reader)
        {
            ImmutableList<Clause> clauses = null;
            int? weight = null;
            UserAttribute? bucketBy = null;
            for (var obj = reader.Object(); obj.Next(ref reader);)
            {
                switch (obj.Name)
                {
                    case var n when n == "clauses":
                        clauses = SerializationHelpers.ReadClauses(ref reader);
                        break;
                    case var n when n == "weight":
                        weight = reader.IntOrNull();
                        break;
                    case var n when n == "bucketBy":
                        var s = reader.StringOrNull();
                        bucketBy = s is null ? (UserAttribute?)null : UserAttribute.ForName(s);
                        break;
                }
            }
            return new SegmentRule(clauses, weight, bucketBy);
        }

    }

    internal static class SerializationHelpers
    {
        internal static readonly IJsonStreamConverter ValueConverter = new LdJsonConverters.LdValueConverter();
        
        internal static void WriteVariationOrRollout(ref ObjectWriter obj, int? variation, Rollout? rollout)
        {
            if (variation.HasValue)
            {
                obj.Name("variation").Int(variation.Value);
            }
            if (rollout.HasValue)
            {
                var rolloutObj = obj.Name("rollout").Object();
                var variationsArr = rolloutObj.Name("variations").Array();
                foreach (var v in rollout.Value.Variations)
                {
                    var variationObj = variationsArr.Object();
                    variationObj.Name("variation").Int(v.Variation);
                    variationObj.Name("weight").Int(v.Weight);
                    variationObj.End();
                }
                variationsArr.End();
                rolloutObj.Name("bucketBy").String(rollout.Value.BucketBy.HasValue ?
                    rollout.Value.BucketBy.Value.AttributeName : null);
                rolloutObj.End();
            }
        }

        internal static void WriteClauses(IValueWriter writer, IEnumerable<Clause> clauses)
        {
            var arr = writer.Array();
            foreach (var c in clauses)
            {
                var clauseObj = arr.Object();
                clauseObj.Name("attribute").String(c.Attribute.AttributeName);
                clauseObj.Name("op").String(c.Op);
                WriteValues(clauseObj.Name("values"), c.Values);
                clauseObj.Name("negate").Bool(c.Negate);
                clauseObj.End();
            }
            arr.End();
        }

        internal static void WriteStrings(IValueWriter writer, IEnumerable<string> values)
        {
            var arr = writer.Array();
            foreach (var v in values)
            {
                arr.String(v);
            }
            arr.End();
        }

        internal static void WriteValues(IValueWriter writer, IEnumerable<LdValue> values)
        {
            var arr = writer.Array();
            foreach (var v in values)
            {
                ValueConverter.WriteJson(v, arr);
            }
            arr.End();
        }

        internal static ImmutableList<Clause> ReadClauses(ref JReader r)
        {
            var builder = ImmutableList.CreateBuilder<Clause>();
            for (var arr = r.ArrayOrNull();  arr.Next(ref r);)
            {
                UserAttribute attribute;
                string op = null;
                ImmutableList<LdValue> values = null;
                bool negate = false;
                for (var obj = r.Object(); obj.Next(ref r);)
                {
                    switch (obj.Name)
                    {
                        case var n when n == "attribute":
                            attribute = UserAttribute.ForName(r.String());
                            break;
                        case var n when n == "op":
                            op = r.String();
                            break;
                        case var n when n == "values":
                            values = ReadValues(ref r);
                            break;
                        case var n when n == "negate":
                            negate = r.Bool();
                            break;
                    }
                }
                builder.Add(new Clause(attribute, op, values, negate));
            }
            return builder.ToImmutable();
        }

        internal static ImmutableList<string> ReadStrings(ref JReader r)
        {
            var builder = ImmutableList.CreateBuilder<string>();
            for (var arr = r.ArrayOrNull();  arr.Next(ref r); )
            {
                builder.Add(r.String());
            }
            return builder.ToImmutable();
        }

        internal static ImmutableList<LdValue> ReadValues(ref JReader r)
        {
            var builder = ImmutableList.CreateBuilder<LdValue>();
            for (var arr = r.ArrayOrNull(); arr.Next(ref r);)
            {
                builder.Add((LdValue)ValueConverter.ReadJson(ref r));
            }
            return builder.ToImmutable();
        }
    }
}
