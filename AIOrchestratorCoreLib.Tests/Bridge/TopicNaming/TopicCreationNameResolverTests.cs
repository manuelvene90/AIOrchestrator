using AIOrchestratorCoreLib.Bridge.TopicNaming;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.TopicNaming;

public class TopicCreationNameResolverTests
{
    [Fact]
    public void Resolve_AUsableName_IsTheName()
    {
        Assert.Equal("TKT · ticket view sync", TopicCreationName_Resolver.Resolve("dvfs-33", "TKT · ticket view sync"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_NoName_FallsBackToTheOrchId(string? wanted)
    {
        Assert.Equal("dvfs-33", TopicCreationName_Resolver.Resolve("dvfs-33", wanted));
    }

    [Fact]
    public void Resolve_ANameTelegramWouldRefuseForLength_FallsBackToTheOrchId()
    {
        var tooLong = new string('x', TopicCreationName_Resolver.MAX_TOPIC_NAME_LENGTH + 1);

        Assert.Equal("dvfs-33", TopicCreationName_Resolver.Resolve("dvfs-33", tooLong));
    }

    [Fact]
    public void Resolve_ANameAtTheCap_IsKept()
    {
        var atCap = new string('x', TopicCreationName_Resolver.MAX_TOPIC_NAME_LENGTH);

        Assert.Equal(atCap, TopicCreationName_Resolver.Resolve("dvfs-33", atCap));
    }

    [Fact]
    public void Resolve_ABlankOrchId_Throws()
    {
        Assert.Throws<ArgumentException>(() => TopicCreationName_Resolver.Resolve(" ", "a name"));
    }
}
