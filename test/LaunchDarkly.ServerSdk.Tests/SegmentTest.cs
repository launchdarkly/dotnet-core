﻿using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using Xunit;
using LaunchDarkly.Client;

namespace LaunchDarkly.Tests
{
    public class SegmentTest
    {
        [Fact]
        public void ExplicitIncludeUser()
        {
            var s = new Segment("test", 1, new List<string> { "foo" }, null, null, null, false);
            var u = User.WithKey("foo");
            Assert.True(s.MatchesUser(u));
        }

        [Fact]
        public void ExplicitExcludeUser()
        {
            var s = new Segment("test", 1, null, new List<string> { "foo" }, null, null, false);
            var u = User.WithKey("foo");
            Assert.False(s.MatchesUser(u));
        }

        [Fact]
        public void ExplicitIncludeHasPrecedence()
        {
            var s = new Segment("test", 1, new List<string> { "foo" }, new List<string> { "foo" }, null, null, false);
            var u = User.WithKey("foo");
            Assert.True(s.MatchesUser(u));
        }

        [Fact]
        public void MatchingRuleWithFullRollout()
        {
            var clause = new Clause("email", "in", new List<JValue> { JValue.CreateString("test@example.com") }, false);
            var rule = new SegmentRule(new List<Clause> { clause }, 100000, null);
            var s = new Segment("test", 1, null, null, null, new List<SegmentRule> { rule }, false);
            var u = User.Builder("foo").Email("test@example.com").Build();
            Assert.True(s.MatchesUser(u));
        }

        [Fact]
        public void MatchingRuleWithZeroRollout()
        {
            var clause = new Clause("email", "in", new List<JValue> { JValue.CreateString("test@example.com") }, false);
            var rule = new SegmentRule(new List<Clause> { clause }, 0, null);
            var s = new Segment("test", 1, null, null, null, new List<SegmentRule> { rule }, false);
            var u = User.Builder("foo").Email("test@example.com").Build();
            Assert.False(s.MatchesUser(u));
        }

        [Fact]
        public void MatchingRuleWithMultipleClauses()
        {
            var clause1 = new Clause("email", "in", new List<JValue> { JValue.CreateString("test@example.com") }, false);
            var clause2 = new Clause("name", "in", new List<JValue> { JValue.CreateString("bob") }, false);
            var rule = new SegmentRule(new List<Clause> { clause1, clause2 }, null, null);
            var s = new Segment("test", 1, null, null, null, new List<SegmentRule> { rule }, false);
            var u = User.Builder("foo").Email("test@example.com").Name("bob").Build();
            Assert.True(s.MatchesUser(u));
        }

        [Fact]
        public void NonMatchingRuleWithMultipleClauses()
        {
            var clause1 = new Clause("email", "in", new List<JValue> { JValue.CreateString("test@example.com") }, false);
            var clause2 = new Clause("name", "in", new List<JValue> { JValue.CreateString("bill") }, false);
            var rule = new SegmentRule(new List<Clause> { clause1, clause2 }, null, null);
            var s = new Segment("test", 1, null, null, null, new List<SegmentRule> { rule }, false);
            var u = User.Builder("foo").Email("test@example.com").Name("bob").Build();
            Assert.False(s.MatchesUser(u));
        }
    }
}
