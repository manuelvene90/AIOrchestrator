using AIOrchestratorCoreLib.Bridge.ReceiptRegistry;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.ReceiptRegistry;

/// <summary>
/// The receipt each topic is waiting to evolve — the ✓ to edit into ✓✓, or the owner message wearing 👀
/// to turn 👌 — moved out of <c>BridgeEngineModel</c> by plan 03 Task 6b.
/// </summary>
public class ReceiptRegistryTests
{
    const long TOPIC_ID = 4242;

    static readonly DateTime NOW = new(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);
    static readonly IReadOnlyList<(string Data, string Label)> GO_BUTTON = [("go:4242", "▶ GO")];

    readonly IReceiptRegistry _registry = ReceiptRegistry_Factory.Create();

    /// <summary>Consumed on read: the next batch gets its own receipt rather than rewriting one already answered.</summary>
    [Fact]
    public void ATick_IsTakenOnce()
    {
        _registry.Remember_Tick(TOPIC_ID, 9001);

        Assert.Equal(9001, _registry.Take_Tick_OrNull(TOPIC_ID));
        Assert.Null(_registry.Take_Tick_OrNull(TOPIC_ID));
    }

    [Fact]
    public void AReaction_IsTakenOnce_AndIsNotATick()
    {
        _registry.Remember_Reaction(TOPIC_ID, 77);

        Assert.Null(_registry.Take_Tick_OrNull(TOPIC_ID));
        Assert.Equal(77, _registry.Take_Reaction_OrNull(TOPIC_ID));
        Assert.Null(_registry.Take_Reaction_OrNull(TOPIC_ID));
    }

    /// <summary>The newest ✓ is the one the delivery edits — the earlier ones of a batch stay ✓.</summary>
    [Fact]
    public void ALaterTick_ReplacesTheEarlierOne()
    {
        _registry.Remember_Tick(TOPIC_ID, 9001);
        _registry.Remember_Tick(TOPIC_ID, 9002);

        Assert.Equal(9002, _registry.Take_Tick_OrNull(TOPIC_ID));
    }

    [Fact]
    public void TheGeneralTopic_IsItsOwnKey()
    {
        _registry.Remember_Tick(null, 9003);

        Assert.Null(_registry.Take_Tick_OrNull(TOPIC_ID));
        Assert.Equal(9003, _registry.Take_Tick_OrNull(null));
    }

    /// <summary>
    /// A PEEK, NOT A TAKE: asking whether the delivery will edit a message must leave the ✓ where the
    /// delivery will find it.
    /// </summary>
    [Fact]
    public void TheRegisteredTick_IsTheOneTheDeliveryWillEdit_AndAskingDoesNotConsumeIt()
    {
        _registry.Remember_Tick(TOPIC_ID, 9001);

        Assert.True(_registry.Is_TheTickTheDeliveryWillEdit(TOPIC_ID, 9001));
        Assert.False(_registry.Is_TheTickTheDeliveryWillEdit(TOPIC_ID, 9000));
        Assert.False(_registry.Is_TheTickTheDeliveryWillEdit(null, 9001));
        Assert.Equal(9001, _registry.Take_Tick_OrNull(TOPIC_ID));
        Assert.False(_registry.Is_TheTickTheDeliveryWillEdit(TOPIC_ID, 9001));
    }

    /// <summary>
    /// A 👀 WAITING IN THE SAME TOPIC WINS AT DELIVERY — the delivery turns it 👌 and returns before any ✓
    /// is edited — so a ✓ registered beside it is not the one the delivery will edit.
    /// </summary>
    [Fact]
    public void ATickBesideAWaitingReaction_IsNotTheOneTheDeliveryWillEdit()
    {
        _registry.Remember_Tick(TOPIC_ID, 9001);
        _registry.Remember_Reaction(TOPIC_ID, 77);

        Assert.False(_registry.Is_TheTickTheDeliveryWillEdit(TOPIC_ID, 9001));
    }

    // ---------------------------------------------------------------------------------------
    // A GO's hold receipt becomes the tick (plan 03 Task 6c)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A TYPED WAIT TOOK THE ✓ to make it "⏸ holding"; the GO that releases the hold gives it back as the
    /// tick, so the delivery edits it into ✓✓. With nothing registered — and when the registered ✓ IS it.
    /// </summary>
    [Fact]
    public void AnAdoptedHoldReceipt_IsTheTickTheDeliveryWillEdit()
    {
        Assert.True(_registry.Adopt_Tick(TOPIC_ID, 9001));
        Assert.True(_registry.Is_TheTickTheDeliveryWillEdit(TOPIC_ID, 9001));

        Assert.True(_registry.Adopt_Tick(TOPIC_ID, 9001));
        Assert.Equal(9001, _registry.Take_Tick_OrNull(TOPIC_ID));
    }

    /// <summary>
    /// REFUSED OVER A DIFFERENT, LIVE ✓ — CHANGED DELIBERATELY in Task 6c fix round 1. This case used to assert
    /// the opposite ("replacing a newer ✓ if one was sent meanwhile"), and that was the review's m1: a ⏸ tapped
    /// on an older ✓ while a newer one was registered made the older one the tick at GO, so the NEWER ✓ — the one
    /// under the owner's last message — kept ⏸ Wait ▶ Send now on a batch that had been delivered. The newer ✓
    /// is the receipt; the older hold receipt goes back to being a ✓ instead (<see cref="IReceiptRegistry.Finish_Hold"/>).
    /// </summary>
    [Fact]
    public void AdoptingOverADifferentLiveTick_IsRefused_AndTheLiveTickStays()
    {
        _registry.Remember_Tick(TOPIC_ID, 9002);

        Assert.False(_registry.Adopt_Tick(TOPIC_ID, 9001));
        Assert.Equal(9002, _registry.Take_Tick_OrNull(TOPIC_ID));
    }

    /// <summary>
    /// REFUSED BESIDE A 👀: the delivery turns that 👌 and never takes the tick, so an adopted one would sit
    /// in the registry until some LATER delivery edited a message that has nothing to do with it.
    /// </summary>
    [Fact]
    public void AdoptingBesideAWaitingReaction_IsRefused_AndRegistersNothing()
    {
        _registry.Remember_Reaction(TOPIC_ID, 77);

        Assert.False(_registry.Adopt_Tick(TOPIC_ID, 9001));
        Assert.Null(_registry.Take_Tick_OrNull(TOPIC_ID));
    }

    // ---------------------------------------------------------------------------------------
    // The text each receipt message is waiting to show (plan 03 Task 6c)
    // ---------------------------------------------------------------------------------------

    /// <summary>With the door open and nobody attempting, a staged edit is attempted by its writer at once.</summary>
    [Fact]
    public void AStagedEdit_WithTheDoorOpen_IsAttemptedAtOnce()
    {
        var attempt = _registry.Stage_Edit_OrNull(9001, "✓✓", buttons: null, "scope", NOW);

        Assert.NotNull(attempt);
        Assert.Equal((9001L, "✓✓", 1, "scope"), (attempt.Value.MessageId, attempt.Value.Text, attempt.Value.AttemptNumber, attempt.Value.LogScope));
        Assert.Null(attempt.Value.Buttons);
        Assert.True(_registry.Has_PendingEdit(9001));
    }

    [Fact]
    public void ALandedEdit_ClearsItsSlot()
    {
        var attempt = _registry.Stage_Edit_OrNull(9001, "✓✓", buttons: null, "scope", NOW)!.Value;

        _registry.Settle_EditLanded(9001, attempt.Version);

        Assert.False(_registry.Has_PendingEdit(9001));
        Assert.Empty(_registry.Take_DueEdits(NOW.AddMinutes(1)));
    }

    /// <summary>
    /// ONE ATTEMPT AT A TIME PER MESSAGE, AND THE NEWEST TEXT IS WHAT FOLLOWS IT. A second writer while the
    /// first is on the wire only replaces the text; when the first lands, the newer text is still owed and
    /// is due at once — a landed OLDER version must never clear it.
    /// </summary>
    [Fact]
    public void ANewerText_StagedWhileOneIsInFlight_WaitsForIt_AndSurvivesItsLanding()
    {
        var first = _registry.Stage_Edit_OrNull(9001, "✓ ⏸ holding · 1 message", GO_BUTTON, "scope", NOW)!.Value;

        Assert.Null(_registry.Stage_Edit_OrNull(9001, "✓✓", buttons: null, "scope", NOW));
        Assert.Empty(_registry.Take_DueEdits(NOW));

        _registry.Settle_EditLanded(9001, first.Version);

        var next = Assert.Single(_registry.Take_DueEdits(NOW));
        Assert.Equal("✓✓", next.Text);
        Assert.Null(next.Buttons);
        Assert.Equal(1, next.AttemptNumber);
    }

    /// <summary>A deferred edit is not handed out before its door, and is handed out AT it — the edge is open.</summary>
    [Fact]
    public void ADeferredEdit_IsDueAtItsDoor_AndNotBefore()
    {
        _registry.Stage_Edit_OrNull(9001, "✓✓", buttons: null, "scope", NOW);
        _registry.Settle_EditDeferred(9001, NOW.AddSeconds(30));

        Assert.Empty(_registry.Take_DueEdits(NOW.AddSeconds(29)));

        var retry = Assert.Single(_registry.Take_DueEdits(NOW.AddSeconds(30)));
        Assert.Equal(2, retry.AttemptNumber);
    }

    /// <summary>
    /// LAST WRITER WINS BEHIND A SHUT DOOR. Two texts staged while the door is shut: only the newer is ever
    /// handed out, and the door stays where the message's edit gap put it — a newer text does not open a
    /// door the older one was turned back from. It starts the retry allowance afresh: it is a new edit.
    /// </summary>
    [Fact]
    public void ANewerText_BehindAShutDoor_ReplacesTheOlder_AndKeepsTheDoor()
    {
        _registry.Stage_Edit_OrNull(9001, "✓ ⏸ holding · 2 messages", GO_BUTTON, "scope", NOW);
        _registry.Settle_EditDeferred(9001, NOW.AddSeconds(30));

        Assert.Null(_registry.Stage_Edit_OrNull(9001, "✓ ⏸ holding · 3 messages", GO_BUTTON, "scope", NOW.AddSeconds(5)));
        Assert.Empty(_registry.Take_DueEdits(NOW.AddSeconds(29)));

        var due = Assert.Single(_registry.Take_DueEdits(NOW.AddSeconds(30)));
        Assert.Equal("✓ ⏸ holding · 3 messages", due.Text);
        Assert.Equal(1, due.AttemptNumber);
    }

    /// <summary>Giving up on a version clears the slot — but never a newer text staged while it was on the wire.</summary>
    [Fact]
    public void AnAbandonedVersion_ClearsItsSlot_ButNeverANewerText()
    {
        var abandoned = _registry.Stage_Edit_OrNull(9001, "old", buttons: null, "scope", NOW)!.Value;
        _registry.Settle_EditAbandoned(9001, abandoned.Version);

        Assert.False(_registry.Has_PendingEdit(9001));

        var inFlight = _registry.Stage_Edit_OrNull(9002, "old", buttons: null, "scope", NOW)!.Value;
        _registry.Stage_Edit_OrNull(9002, "new", buttons: null, "scope", NOW);
        _registry.Settle_EditAbandoned(9002, inFlight.Version);

        Assert.Equal("new", Assert.Single(_registry.Take_DueEdits(NOW)).Text);
    }

    /// <summary>The slot is per MESSAGE: an old receipt waiting for its door does not hold another message back.</summary>
    [Fact]
    public void TwoMessages_HaveTwoSlots()
    {
        _registry.Stage_Edit_OrNull(9001, "a", buttons: null, "scope", NOW);
        _registry.Settle_EditDeferred(9001, NOW.AddSeconds(30));

        Assert.NotNull(_registry.Stage_Edit_OrNull(9002, "b", buttons: null, "scope", NOW));
    }

    // ---------------------------------------------------------------------------------------
    // One hold, one receipt — and every route that ends a hold finishes it (Task 6c fix round 1)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void AHoldReceipt_IsFound_AndCountsUp()
    {
        _registry.Remember_HoldReceipt(TOPIC_ID, 9001, heldCount: 1);

        Assert.Equal((9001L, 1), _registry.Find_HoldReceipt_OrNull(TOPIC_ID));
        Assert.Equal((9001L, 2), _registry.Count_HeldMessage_OrNull(TOPIC_ID));
        Assert.Equal((9001L, 2), _registry.Find_HoldReceipt_OrNull(TOPIC_ID));
        Assert.Null(_registry.Find_HoldReceipt_OrNull(null));
    }

    /// <summary>A hold with no message (entered from the bar) still counts, and has nothing to redraw or find.</summary>
    [Fact]
    public void AHoldWithNoMessage_CountsButHasNoReceipt()
    {
        _registry.Remember_HoldReceipt(TOPIC_ID, messageId: null, heldCount: 0);

        Assert.Null(_registry.Count_HeldMessage_OrNull(TOPIC_ID));
        Assert.Null(_registry.Find_HoldReceipt_OrNull(TOPIC_ID));
        Assert.Equal((false, (long?)null), _registry.Finish_Hold(TOPIC_ID, deliveringHeldMessages: true));
    }

    /// <summary>A GO that ends no hold — Send now on a ✓ — finishes nothing and rewrites nothing.</summary>
    [Fact]
    public void AGoWithNoHoldReceipt_FinishesNothing()
    {
        _registry.Remember_Tick(TOPIC_ID, 9002);

        Assert.Equal((false, (long?)null), _registry.Finish_Hold(TOPIC_ID, deliveringHeldMessages: true));
        Assert.Equal(9002, _registry.Take_Tick_OrNull(TOPIC_ID));
    }

    /// <summary>
    /// THE DOUBLE TICK'S ROUTE: held messages are being delivered and nothing else is registered (a typed WAIT
    /// took the ✓), or the registered ✓ is this very message (a tapped ⏸) — the hold receipt becomes THE ✓, the
    /// delivery edits it into ✓✓, and nothing is rewritten first. The hold is over: it is found no more.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AHoldReceipt_BeingDelivered_IsAdopted_WhenNothingElseIsRegistered(bool itIsAlsoTheRegisteredTick)
    {
        if (itIsAlsoTheRegisteredTick)
            _registry.Remember_Tick(TOPIC_ID, 9001);

        _registry.Remember_HoldReceipt(TOPIC_ID, 9001, heldCount: 2);

        Assert.Equal((true, (long?)null), _registry.Finish_Hold(TOPIC_ID, deliveringHeldMessages: true));
        Assert.True(_registry.Is_TheTickTheDeliveryWillEdit(TOPIC_ID, 9001));
        Assert.Null(_registry.Find_HoldReceipt_OrNull(TOPIC_ID));
    }

    /// <summary>An EMPTY hold delivers nothing to finish it, so its receipt goes back to being a ✓ — and is never left as a tick.</summary>
    [Fact]
    public void AnEmptyHold_IsRewritten_NeverAdopted()
    {
        _registry.Remember_HoldReceipt(TOPIC_ID, 9001, heldCount: 0);

        Assert.Equal((true, (long?)9001), _registry.Finish_Hold(TOPIC_ID, deliveringHeldMessages: false));
        Assert.Null(_registry.Take_Tick_OrNull(TOPIC_ID));
    }

    /// <summary>
    /// ANOTHER RECEIPT WILL CARRY THE DELIVERY — a newer ✓ (it becomes ✓✓) or a 👀 (it becomes 👌) — so the hold
    /// receipt is rewritten back to a ✓ rather than adopted over it, and the other receipt is left in place.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AHoldReceipt_BesideAnotherTickOrAReaction_IsRewritten(bool aReactionWaits)
    {
        if (aReactionWaits)
            _registry.Remember_Reaction(TOPIC_ID, 77);
        else
            _registry.Remember_Tick(TOPIC_ID, 9002);

        _registry.Remember_HoldReceipt(TOPIC_ID, 9001, heldCount: 2);

        Assert.Equal((true, (long?)9001), _registry.Finish_Hold(TOPIC_ID, deliveringHeldMessages: true));
        Assert.False(_registry.Is_TheTickTheDeliveryWillEdit(TOPIC_ID, 9001));

        if (aReactionWaits)
            Assert.Equal(77, _registry.Take_Reaction_OrNull(TOPIC_ID));
        else
            Assert.Equal(9002, _registry.Take_Tick_OrNull(TOPIC_ID));
    }

    /// <summary>A hold is finished once: the second route to end it finds nothing left to do.</summary>
    [Fact]
    public void AHold_IsFinishedOnce()
    {
        _registry.Remember_HoldReceipt(TOPIC_ID, 9001, heldCount: 0);
        _registry.Finish_Hold(TOPIC_ID, deliveringHeldMessages: false);

        Assert.Equal((false, (long?)null), _registry.Finish_Hold(TOPIC_ID, deliveringHeldMessages: true));
    }
}
