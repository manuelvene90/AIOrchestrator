using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// The owner, 2026-08-15: "Can't you add a button somewhere that if I click it starts a wait?
/// Clicking a button is faster than typing wait."
///
/// It is not only convenience. Typing WAIT loses a race with the aggregation window — every second
/// spent typing is a second the message is closer to going out — so the tap is the difference
/// between the hold working and not.
/// </summary>
public class HoldButtonDataTests
{
    [Fact]
    public void Hold_RoundTripsWithItsTopic()
    {
        var parsed = HoldButton_Data.Parse_OrNull(HoldButton_Data.Build(HoldButtonActions.Hold, 4242L));

        Assert.Equal((HoldButtonActions.Hold, 4242L), parsed);
    }

    [Fact]
    public void Go_RoundTripsWithItsTopic()
    {
        var parsed = HoldButton_Data.Parse_OrNull(HoldButton_Data.Build(HoldButtonActions.Go, 4242L));

        Assert.Equal((HoldButtonActions.Go, 4242L), parsed);
    }

    /// <summary>
    /// The General topic has no thread id, and it must survive the round trip as null rather than as
    /// zero — everything downstream treats a thread id as "which topic", and 0 is not one.
    /// </summary>
    [Fact]
    public void TheGeneralTopic_RoundTripsAsNull()
    {
        var parsed = HoldButton_Data.Parse_OrNull(HoldButton_Data.Build(HoldButtonActions.Hold, null));

        Assert.Equal((HoldButtonActions.Hold, (long?)null), parsed);
    }

    /// <summary>
    /// ANYTHING ELSE IS NOT OURS. A tap this cannot read has to fall through to the option handler
    /// untouched — swallowing it as a malformed hold would eat the owner's answer to a question,
    /// which is the one tap in this system that cannot be repeated.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("opt-17")]
    [InlineData("close:crm-2")]
    [InlineData("hold")]
    [InlineData("hold:")]
    [InlineData("hold:not-a-number")]
    [InlineData("gone:4242")]
    [InlineData("HOLD:4242")]
    public void AnythingElse_IsNotOurs(string? callbackData)
    {
        Assert.Null(HoldButton_Data.Parse_OrNull(callbackData));
    }

    /// <summary>Telegram's hard cap. A payload over it is rejected at send time, on the phone.</summary>
    [Fact]
    public void ThePayload_FitsTelegramsSixtyFourByteLimit()
    {
        var built = HoldButton_Data.Build(HoldButtonActions.Hold, long.MaxValue);

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(built) <= 64, $"callback data too long: '{built}'");
    }

    /// <summary>
    /// The two verbs must not be prefixes of one another, or the parse order would decide the
    /// meaning — a bug that only shows up once somebody adds a third verb.
    /// </summary>
    [Fact]
    public void TheTwoVerbs_AreDistinguishable()
    {
        var hold = HoldButton_Data.Build(HoldButtonActions.Hold, 1L);
        var go = HoldButton_Data.Build(HoldButtonActions.Go, 1L);

        Assert.NotEqual(hold, go);
        Assert.False(hold.StartsWith(go, System.StringComparison.Ordinal));
        Assert.False(go.StartsWith(hold, System.StringComparison.Ordinal));
    }
}
