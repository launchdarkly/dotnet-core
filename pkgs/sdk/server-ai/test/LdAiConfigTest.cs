using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.DataModel;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Ai;

public class LdAiConfigTest
{
    [Fact]
    public void CanDisableAndEnableConfig()
    {
        var config1 = LdAiConfig.New().Enable().Disable().Build();
        Assert.False(config1.Enabled);

        var config2 = LdAiConfig.New().Disable().Enable().Build();
        Assert.True(config2.Enabled);

        var config3 = LdAiConfig.New().SetEnabled(true).SetEnabled(false).Build();
        Assert.False(config3.Enabled);

        var config4 = LdAiConfig.New().SetEnabled(false).SetEnabled(true).Build();
        Assert.True(config4.Enabled);

        var config5 = LdAiConfig.New().SetEnabled(true).Disable().Build();
        Assert.False(config5.Enabled);

        var config6 = LdAiConfig.New().SetEnabled(false).Enable().Build();
        Assert.True(config6.Enabled);
    }

    [Fact]
    public void CanAddPromptMessages()
    {
        var config = LdAiConfig.New()
            .AddMessage("Hello")
            .AddMessage("World", Role.System)
            .AddMessage("!", Role.Assistant)
            .Build();

        Assert.Collection(config.Messages,
            message =>
            {
                Assert.Equal("Hello", message.Content);
                Assert.Equal(Role.User, message.Role);
            },
            message =>
            {
                Assert.Equal("World", message.Content);
                Assert.Equal(Role.System, message.Role);
            },
            message =>
            {
                Assert.Equal("!", message.Content);
                Assert.Equal(Role.Assistant, message.Role);
            });
    }

    [Fact]
    public void CanSetModelParams()
    {
        var config = LdAiConfig.New()
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
        var config = LdAiConfig.New().SetModelId("awesome-model").Build();
        Assert.Equal("awesome-model", config.Model.Name);
    }

    [Fact]
    public void CanSetModelProviderId()
    {
        var config = LdAiConfig.New()
            .SetModelProviderId("amazing-provider")
            .Build();

        Assert.Equal("amazing-provider", config.Provider.Name);
    }
}
