using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

public class TelegramInboundModesTests
{
    [Theory]
    [InlineData("off")]
    [InlineData("OFF")]
    [InlineData("Off")]
    [InlineData("  off  ")]
    [InlineData(" OfF ")]
    public void OffInAnyCasingOrSurroundingWhitespace_ParsesToOff(string text)
    {
        var mode = TelegramInbound_Modes.Parse_OrPoll(text);

        Assert.Equal(TelegramInboundModes.Off, mode);
    }

    /// <summary>
    /// Poll is the SAFE direction: a host that silently stops polling is a phone that silently stops
    /// working, and it cannot even report why, since it is not polling and never sees the 409 that
    /// would have explained it. So an absent, empty, or unrecognised value must never read as Off.
    /// </summary>
    [Theory]
    [InlineData("poll")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("of")]
    [InlineData("mirror")]
    public void PollNullOrAnUnrecognisedValue_ParsesToPollTheSafeDirection(string? text)
    {
        var mode = TelegramInbound_Modes.Parse_OrPoll(text);

        Assert.Equal(TelegramInboundModes.Poll, mode);
    }

    [Fact]
    public void Describe_RoundTripsBothValues()
    {
        Assert.Equal(TelegramInboundModes.Poll, TelegramInbound_Modes.Parse_OrPoll(TelegramInbound_Modes.Describe(TelegramInboundModes.Poll)));
        Assert.Equal(TelegramInboundModes.Off, TelegramInbound_Modes.Parse_OrPoll(TelegramInbound_Modes.Describe(TelegramInboundModes.Off)));
    }
}
