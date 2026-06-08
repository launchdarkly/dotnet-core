using System;
using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Ai.Config;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Ai;

public class LdAiCompletionConfigDefaultTest
{
    [Fact]
    public void CanDisableAndEnableConfig()
    {
        var config1 = LdAiCompletionConfigDefault.New().Enable().Disable().Build();
        Assert.False(config1.Enabled);

        var config2 = LdAiCompletionConfigDefault.New().Disable().Enable().Build();
        Assert.True(config2.Enabled);

        var config3 = LdAiCompletionConfigDefault.New().SetEnabled(true).SetEnabled(false).Build();
        Assert.False(config3.Enabled);

        var config4 = LdAiCompletionConfigDefault.New().SetEnabled(false).SetEnabled(true).Build();
        Assert.True(config4.Enabled);

        var config5 = LdAiCompletionConfigDefault.New().SetEnabled(true).Disable().Build();
        Assert.False(config5.Enabled);

        var config6 = LdAiCompletionConfigDefault.New().SetEnabled(false).Enable().Build();
        Assert.True(config6.Enabled);
    }

    [Fact]
    public void CanAddPromptMessages()
    {
        var config = LdAiCompletionConfigDefault.New()
            .AddMessage("Hello")
            .AddMessage("World", LdAiConfigTypes.Role.System)
            .AddMessage("!", LdAiConfigTypes.Role.Assistant)
            .Build();

        Assert.Collection(config.Messages,
            message =>
            {
                Assert.Equal("Hello", message.Content);
                Assert.Equal(LdAiConfigTypes.Role.User, message.Role);
            },
            message =>
            {
                Assert.Equal("World", message.Content);
                Assert.Equal(LdAiConfigTypes.Role.System, message.Role);
            },
            message =>
            {
                Assert.Equal("!", message.Content);
                Assert.Equal(LdAiConfigTypes.Role.Assistant, message.Role);
            });
    }

    [Fact]
    public void CanSetModelParams()
    {
        var config = LdAiCompletionConfigDefault.New()
            .SetModelParam("foo", LdValue.Of("bar"))
            .SetModelParam("baz", LdValue.Of(42))
            .SetCustomModelParam("foo", LdValue.Of("baz"))
            .SetCustomModelParam("baz", LdValue.Of(43))
            .Build();

        Assert.Equal(LdValue.Of("bar"), config.Model.Parameters["foo"]);
        Assert.Equal(LdValue.Of(42), config.Model.Parameters["baz"]);

        Assert.Equal(LdValue.Of("baz"), config.Model.Custom["foo"]);
        Assert.Equal(LdValue.Of(43), config.Model.Custom["baz"]);
    }

    [Fact]
    public void CanSetModelId()
    {
        var config = LdAiCompletionConfigDefault.New().SetModelName("awesome-model").Build();
        Assert.Equal("awesome-model", config.Model.Name);
    }

    [Fact]
    public void CanSetModelProviderId()
    {
        var config = LdAiCompletionConfigDefault.New()
            .SetModelProviderName("amazing-provider")
            .Build();

        Assert.Equal("amazing-provider", config.Provider.Name);
    }

    [Fact]
    public void ModelParameterDictionariesCannotBeMutatedViaDowncast()
    {
        // ModelConfig.Parameters and .Custom are typed as IReadOnlyDictionary<>, so the
        // public contract is "read-only". A consumer that downcasts to IDictionary<>
        // must not be able to mutate the stored map. ImmutableDictionary<> satisfies
        // both shapes: the cast still succeeds, but writes throw at runtime.
        var config = LdAiCompletionConfigDefault.New()
            .SetModelParam("temperature", LdValue.Of(0.7))
            .SetCustomModelParam("flavor", LdValue.Of("spicy"))
            .Build();

        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, LdValue>)config.Model.Parameters)["temperature"] = LdValue.Of(2.0));
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, LdValue>)config.Model.Custom)["flavor"] = LdValue.Of("mild"));

        Assert.Equal(0.7, config.Model.Parameters["temperature"].AsDouble);
        Assert.Equal("spicy", config.Model.Custom["flavor"].AsString);
    }
}
