using System;
using Xunit;
using static LaunchDarkly.Sdk.Server.Internal.Evaluation.EvaluatorTypes;

namespace LaunchDarkly.Sdk.Server.Internal.Evaluation
{
    public class EvaluatorTypesLazyStackTest
    {
        [Fact]
        public void CanPushAndPopSingleItem()
        {
            var stack = new LazyStack<string>();

            stack.Push("item1");
    
            Assert.Equal("item1", stack.Pop());
        }

        [Fact]
        public void CanPushAndPopMultipleItems()
        {
            var stack = new LazyStack<string>();
            
            stack.Push("item1");
            stack.Push("item2");
            stack.Push("item3");
            
            Assert.Equal("item3", stack.Pop());
            Assert.Equal("item2", stack.Pop());
            Assert.Equal("item1", stack.Pop());
        }

        [Fact]
        public void PopEmptyStackThrowsException()
        {
            var stack = new LazyStack<string>();
            
            Assert.Throws<InvalidOperationException>(() => stack.Pop());
        }

        [Fact]
        public void ContainsReturnsTrueForExistingItem()
        {
            var stack = new LazyStack<string>();
            
            stack.Push("item1");
            stack.Push("item2");
            
            Assert.True(stack.Contains("item1"));
            Assert.True(stack.Contains("item2"));
        }

        [Fact]
        public void ContainsReturnsFalseForNonExistingItem()
        {
            var stack = new LazyStack<string>();
            
            stack.Push("item1");
            
            Assert.False(stack.Contains("item2"));
        }

        [Fact]
        public void ContainsWorksAfterPop()
        {
            var stack = new LazyStack<string>();
            
            stack.Push("item1");
            stack.Push("item2");
            stack.Pop();
            
            Assert.True(stack.Contains("item1"));
            Assert.False(stack.Contains("item2"));
        }

        [Fact]
        public void ContainsReturnsFalseForEmptyStack()
        {
            var stack = new LazyStack<string>();
            
            Assert.False(stack.Contains("item1"));
        }

        [Fact]
        public void CanPushSameItemMultipleTimes()
        {
            var stack = new LazyStack<string>();
            
            stack.Push("duplicate");
            stack.Push("other");
            stack.Push("duplicate");
            
            Assert.True(stack.Contains("duplicate"));
            Assert.Equal("duplicate", stack.Pop());
            Assert.Equal("other", stack.Pop());
            Assert.Equal("duplicate", stack.Pop());
        }

        [Fact]
        public void WorksWithDifferentTypes()
        {
            var intStack = new LazyStack<int>();
            
            intStack.Push(1);
            intStack.Push(2);
            
            Assert.Equal(2, intStack.Pop());
            Assert.Equal(1, intStack.Pop());

            var strStack = new LazyStack<string>();
            
            strStack.Push("one");
            strStack.Push("two");
            
            Assert.Equal("two", strStack.Pop());
            Assert.Equal("one", strStack.Pop());
        }

        [Fact]
        public void ContainsReturnsFalseAfterLazyTransition()
        {
            var stack = new LazyStack<string>();
            
            // Push enough items to trigger transition to list
            stack.Push("A");
            stack.Push("B");

            Assert.True(stack.Contains("A"));

            stack.Pop(); // Removes "B"
            Assert.True(stack.Contains("A"));
            
            stack.Pop(); // Removes "A"
            Assert.False(stack.Contains("A"));
        }
    }
} 