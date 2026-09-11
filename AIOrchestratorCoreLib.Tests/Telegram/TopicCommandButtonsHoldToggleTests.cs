using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// THE HOLD TOGGLE ON THE PULSE BAR — brief D.
///
/// <para>
/// ⏸ Wait used to ride the ✓ receipt, which landed under whatever the owner had just sent. Brief D
/// replaces that receipt with a reaction, so the button has no message to hang on any more and
/// moves to the one message that is always there. It is NOT also kept under the owner's messages:
/// one toggle in two homes is CLAUDE.md decision 12, and the owner ruled the same way on
/// 2026-09-10.
/// </para>
/// </summary>
public class TopicCommandButtonsHoldToggleTests
{
    const long TOPIC = 4242;

    [Fact]
    public void TheBar_OffersWait_WhenNothingIsHeld()
    {
        var buttons = TopicCommandButtons.Build_ForTopic(TOPIC, isHolding: false, heldCount: 0);

        var toggle = buttons[^1];

        Assert.Equal(HoldButton_Data.HOLD_LABEL, toggle.Label);
        Assert.Equal((HoldButtonActions.Hold, TOPIC), HoldButton_Data.Parse_OrNull(toggle.Data));
    }

    /// <summary>
    /// THE COUNT IS THE POINT. It is the only thing on the bar that tells the owner the hold is
    /// actually catching messages rather than merely switched on — and, because the bar reposts
    /// when its text changes, it is what brings the button back under their messages exactly while
    /// they are holding.
    /// </summary>
    [Fact]
    public void TheBar_OffersGoWithTheHeldCount_WhileHolding()
    {
        var toggle = TopicCommandButtons.Build_ForTopic(TOPIC, isHolding: true, heldCount: 3)[^1];

        Assert.Equal("⏸ 3 held · ▶ GO", toggle.Label);
        Assert.Equal((HoldButtonActions.Go, TOPIC), HoldButton_Data.Parse_OrNull(toggle.Data));
    }

    /// <summary>Holding with nothing caught yet says GO plainly — "0 held" is noise.</summary>
    [Fact]
    public void HoldingWithNothingCaughtYet_SaysGoPlainly()
    {
        Assert.Equal(HoldButton_Data.GO_LABEL, TopicCommandButtons.Describe_ReleaseLabel(0));
        Assert.Equal(HoldButton_Data.GO_LABEL, TopicCommandButtons.Describe_ReleaseLabel(-1));
        Assert.Equal("⏸ 1 held · ▶ GO", TopicCommandButtons.Describe_ReleaseLabel(1));
    }

    /// <summary>
    /// The toggle is ADDED to the six, not one of them: the bar grows to four rows and no existing
    /// button was displaced to make room.
    /// </summary>
    [Fact]
    public void TheToggleIsAddedToTheBar_NotSubstitutedForACommand()
    {
        var plain = TopicCommandButtons.Build_ForTopic(TOPIC);
        var withToggle = TopicCommandButtons.Build_ForTopic(TOPIC, isHolding: false, heldCount: 0);

        Assert.Equal(plain.Count + 1, withToggle.Count);
        Assert.Equal(plain, withToggle.Take(plain.Count));
    }

    /// <summary>
    /// The toggle rides the EXISTING hold payload family, so the tap handler needs nothing new —
    /// and, just as important, TopicCommandButtons' own parser must not claim it.
    /// </summary>
    [Fact]
    public void TheTogglesPayload_BelongsToTheHoldFamily_NotTheCommandBar()
    {
        var toggle = TopicCommandButtons.Build_ForTopic(TOPIC, isHolding: true, heldCount: 2)[^1];

        Assert.NotNull(HoldButton_Data.Parse_OrNull(toggle.Data));
        Assert.Null(TopicCommandButtons.Parse_OrNull(toggle.Data));
    }
}

/// <summary>
/// The two reactions the bridge sets, and the set Telegram actually permits — brief D.
/// </summary>
public class OwnerReactionEmojiTests
{
    /// <summary>
    /// ✅ IS NOT PERMITTED, which is the whole reason this constant exists: it is the obvious first
    /// choice for "done" and Telegram answers it with a 400.
    /// </summary>
    [Fact]
    public void TheCheckMarkIsNotOne_WhichIsWhyTheSetIsWrittenDown()
    {
        Assert.False(OwnerReaction_Emoji.Is_Permitted("✅"));
        Assert.False(OwnerReaction_Emoji.Is_Permitted("✓"));
    }

    [Fact]
    public void BothReactionsTheBridgeSetsArePermitted()
    {
        Assert.True(OwnerReaction_Emoji.Is_Permitted(OwnerReaction_Emoji.RECEIVED));
        Assert.True(OwnerReaction_Emoji.Is_Permitted(OwnerReaction_Emoji.PICKED_UP));
        Assert.NotEqual(OwnerReaction_Emoji.RECEIVED, OwnerReaction_Emoji.PICKED_UP);
    }
}
