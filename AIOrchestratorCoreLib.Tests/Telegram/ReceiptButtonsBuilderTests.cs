using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// THE ✓ RECEIPT OFFERS BOTH DIRECTIONS (plan 03 Task 6b, owner request 2026-09-14 and 2026-09-23):
/// wait for more, or send what is there right now — *"a new button that lets me tell the app to
/// forward the message to the solo immediately without waiting for the buffer time to pass."*
///
/// <para>
/// ▶ Send now is GO under another label — the engine already delivers a GO with no window — so the
/// payload must round-trip as Go for the SAME topic, or the tap releases nothing. And it is DELIVERY,
/// not the hold toggle (ruling R2): it rides every ✓, wherever the toggle lives, while ⏸ Wait still
/// appears only when the toggle's one placement is the receipt.
/// </para>
/// </summary>
public class ReceiptButtonsBuilderTests
{
    const long TOPIC_ID = 4242;

    [Fact]
    public void TheTick_CarriesWaitThenSendNow_AndSendNowIsGoForTheSameTopic()
    {
        var buttons = ReceiptButtons_Builder.Build_ForTick(TOPIC_ID, holdToggleOnTheBar: false);

        Assert.Equal(2, buttons.Count);
        Assert.Equal(HoldButton_Data.HOLD_LABEL, buttons[0].Label);
        Assert.Equal(HoldButton_Data.SEND_NOW_LABEL, buttons[1].Label);
        Assert.Equal((HoldButtonActions.Hold, (long?)TOPIC_ID), HoldButton_Data.Parse_OrNull(buttons[0].Data));
        Assert.Equal((HoldButtonActions.Go, (long?)TOPIC_ID), HoldButton_Data.Parse_OrNull(buttons[1].Data));
    }

    /// <summary>
    /// R2: with the toggle on PULSE the ✓ carries no ⏸ Wait (one toggle, one place — decision 12), and
    /// still carries ▶ Send now, because Send now is not the toggle.
    /// </summary>
    [Fact]
    public void WithTheToggleOnTheBar_TheTickCarriesSendNowAlone()
    {
        var buttons = ReceiptButtons_Builder.Build_ForTick(TOPIC_ID, holdToggleOnTheBar: true);

        var sendNow = Assert.Single(buttons);
        Assert.Equal(HoldButton_Data.SEND_NOW_LABEL, sendNow.Label);
        Assert.Equal(HoldButton_Data.Build(HoldButtonActions.Go, TOPIC_ID), sendNow.Data);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheTickInGeneral_KeysEveryButtonAsTheGeneralTopic(bool holdToggleOnTheBar)
    {
        var buttons = ReceiptButtons_Builder.Build_ForTick(null, holdToggleOnTheBar);

        Assert.NotEmpty(buttons);
        Assert.All(buttons, button => Assert.Null(HoldButton_Data.Parse_OrNull(button.Data)!.Value.MessageThreadId));
    }

    /// <summary>
    /// The one label that differs between two buttons with the same payload: ▶ GO releases a hold,
    /// ▶ Send now skips the window. Were they spelled alike, the owner could not tell a held receipt from
    /// a waiting one, and a label search could not either.
    /// </summary>
    [Fact]
    public void SendNow_IsLabelledApartFromTheRelease()
    {
        Assert.NotEqual(HoldButton_Data.GO_LABEL, HoldButton_Data.SEND_NOW_LABEL);
        Assert.DoesNotContain(HoldButton_Data.GO_LABEL, HoldButton_Data.SEND_NOW_LABEL, StringComparison.Ordinal);
    }

    /// <summary>
    /// AFTER A TAP: a hold turns the receipt into the release (▶ GO — while held, GO already means
    /// "send what is held, now"); a GO turns it back into a ✓, which offers what every ✓ offers — never a
    /// lone ⏸ Wait, because the next message may be one to hold or one to send at once.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AfterATap_AHoldOffersTheRelease_AndAGoOffersWhatATickOffers(bool holdToggleOnTheBar)
    {
        Assert.Equal(
            ReceiptButtons_Builder.Build_ForHoldReceipt(HoldButtonActions.Go, TOPIC_ID, holdToggleOnTheBar),
            ReceiptButtons_Builder.Build_AfterTap(HoldButtonActions.Hold, TOPIC_ID, holdToggleOnTheBar));

        Assert.Equal(
            ReceiptButtons_Builder.Build_ForTick(TOPIC_ID, holdToggleOnTheBar),
            ReceiptButtons_Builder.Build_AfterTap(HoldButtonActions.Go, TOPIC_ID, holdToggleOnTheBar));
    }

    /// <summary>
    /// A HOLD RECEIPT IS UNCHANGED BY SEND NOW: while held there is one way out, ▶ GO, and with the
    /// toggle on PULSE that way out is PULSE's own — the receipt carries nothing (plan 03 Task 5).
    /// </summary>
    [Fact]
    public void AHoldReceipt_CarriesOnlyTheRelease_OrNothingWhenTheToggleIsOnTheBar()
    {
        var onTheReceipt = ReceiptButtons_Builder.Build_ForHoldReceipt(HoldButtonActions.Go, TOPIC_ID, holdToggleOnTheBar: false);

        Assert.Equal([(HoldButton_Data.Build(HoldButtonActions.Go, TOPIC_ID), HoldButton_Data.GO_LABEL)], onTheReceipt);
        Assert.Empty(ReceiptButtons_Builder.Build_ForHoldReceipt(HoldButtonActions.Go, TOPIC_ID, holdToggleOnTheBar: true));
    }
}
