using AIOrchestratorCoreLib.Limits.ClaudeAccount;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Limits;

/// <summary>
/// The logged-in account out of the CLI's global config — and every shape it cannot read answers
/// null, because null never lifts a pause.
/// </summary>
public class ClaudeAccountParserTests
{
    [Fact]
    public void TheOAuthAccountUuid_IsTheAccountId()
    {
        var json = "{\"numStartups\":3,\"oauthAccount\":{\"accountUuid\":\"178b4690-4fdf-4bb9-ae97-8b69720d06ee\",\"emailAddress\":\"x@example.com\"}}";

        Assert.Equal("178b4690-4fdf-4bb9-ae97-8b69720d06ee", ClaudeAccount_Parser.Read_AccountId_OrNull(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"numStartups\":3}")]
    [InlineData("{\"oauthAccount\":null}")]
    [InlineData("{\"oauthAccount\":{\"accountUuid\":\"  \"}}")]
    [InlineData("{\"oauthAccount\":{\"accountUuid\":42}}")]
    [InlineData("{\"oauthAccount\":{\"accountUu")]
    public void AnythingElse_IsCannotTell(string json)
    {
        Assert.Null(ClaudeAccount_Parser.Read_AccountId_OrNull(json));
    }

    [Fact]
    public void AMissingFile_IsCannotTell()
    {
        var reader = ClaudeAccountReader_Factory.Create_FromFile(Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.json"));

        Assert.Null(reader.Read_AccountId_OrNull());
    }
}
