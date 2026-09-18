using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

public class PauseLiftButtonDataTests
{
    [Fact]
    public void Build_ThenParse_RoundTripsBothButtons()
    {
        Assert.Equal((true, "abc123"), PauseLiftButton_Data.Parse_OrNull(PauseLiftButton_Data.Build_Lift("abc123")));
        Assert.Equal((false, "abc123"), PauseLiftButton_Data.Parse_OrNull(PauseLiftButton_Data.Build_Keep("abc123")));
    }

    /// <summary>Anything that is not ours falls through untouched — the generic path must still see its own payloads.</summary>
    [Theory]
    [InlineData("opt-1-abc")]
    [InlineData("close-yes-abc")]
    [InlineData("model:orch:sup:opus")]
    [InlineData("hold:1")]
    [InlineData("")]
    [InlineData(null)]
    public void AForeignPayload_IsNull(string? data)
    {
        Assert.Null(PauseLiftButton_Data.Parse_OrNull(data));
    }
}
