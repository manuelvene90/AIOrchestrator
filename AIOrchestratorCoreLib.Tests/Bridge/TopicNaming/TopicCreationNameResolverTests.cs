using AIOrchestratorCoreLib.Bridge.TopicNaming;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
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
    public void IsNameRefused_ATelegram400_IsARefusal()
    {
        Assert.True(TopicCreationName_Resolver.Is_NameRefused(
            new TelegramApiException(400, "Telegram 'createForumTopic' failed with HTTP 400: Bad Request: TOPIC_NAME_INVALID")));
    }

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    public void IsNameRefused_ARetryableTelegramAnswer_IsNot(int statusCode)
    {
        Assert.False(TopicCreationName_Resolver.Is_NameRefused(new TelegramApiException(statusCode, $"HTTP {statusCode}")));
    }

    [Fact]
    public void IsNameRefused_TopicNotModified_IsNot()
    {
        Assert.False(TopicCreationName_Resolver.Is_NameRefused(
            new TelegramApiException(400, "Telegram failed with HTTP 400: {\"ok\":false,\"error_code\":400,\"description\":\"Bad Request: TOPIC_NOT_MODIFIED\"}")));
    }

    /// <summary>The review's case: the client's plain Exception after a 200 it could not read — the topic may exist.</summary>
    [Fact]
    public void IsNameRefused_APlainException_IsNot()
    {
        Assert.False(TopicCreationName_Resolver.Is_NameRefused(new Exception("createForumTopic response has no result.message_thread_id: {}")));
    }

    [Fact]
    public void IsNameRefused_ATimeoutOrADroppedConnection_IsNot()
    {
        Assert.False(TopicCreationName_Resolver.Is_NameRefused(new TaskCanceledException("timeout")));
        Assert.False(TopicCreationName_Resolver.Is_NameRefused(new HttpRequestException("connection reset")));
    }

    [Fact]
    public void Resolve_ABlankOrchId_Throws()
    {
        Assert.Throws<ArgumentException>(() => TopicCreationName_Resolver.Resolve(" ", "a name"));
    }
}
