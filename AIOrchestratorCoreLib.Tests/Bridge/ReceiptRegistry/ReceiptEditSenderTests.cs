using AIOrchestratorCoreLib.Bridge.ReceiptRegistry;
using AIOrchestratorCoreLib.Telegram.TelegramSendBudget;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.ReceiptRegistry;

/// <summary>
/// A RECEIPT EDIT HELD BY THE PER-MESSAGE GAP IS RETRIED, NEVER DROPPED — and a refusal no wait will fix is
/// still the writer's to handle (plan 03 Task 6c). Driven over the real gate with a three-second gap, the one
/// the engine probes use: the fake refuses exactly what the real client would refuse, before the call. It was
/// one second until fix round 1, and a stall of a second on a loaded machine — between two writes, or between
/// a write and the "before the door" drain — turned a correct run red.
/// </summary>
public class ReceiptEditSenderTests
{
    const long RECEIPT_ID = 4321;
    const string SCOPE = "orch-1";

    static readonly TimeSpan EDIT_GAP = TimeSpan.FromSeconds(3);
    static readonly TimeSpan PAST_THE_GAP = EDIT_GAP + TimeSpan.FromMilliseconds(300);
    static readonly IReadOnlyList<(string Data, string Label)> GO_BUTTON = [("go:77", "▶ GO")];

    readonly IReceiptRegistry _receipts = ReceiptRegistry_Factory.Create();
    readonly ScriptedInbound_Fake _telegram = new();
    readonly RecordingLog_Fake _log = new();

    public ReceiptEditSenderTests()
    {
        _telegram.Gate_EditsThrough(TelegramSendBudget_Factory.Create_WithEditGap(EDIT_GAP));
    }

    /// <summary>
    /// THE DEFECT, AT ITS SMALLEST: a second edit inside the gap is held by the client. The writer is not
    /// told it failed — it did not — and the drain lands it, keyboard and all, once the door has opened;
    /// never before.
    /// </summary>
    [Fact]
    public async Task AHeldEdit_ReturnsQuietly_AndLandsWhenItsDoorOpens()
    {
        await Write_Async("✓ ⏸ holding · 1 message", GO_BUTTON);
        await Write_Async("✓ ⏸ holding · 2 messages", GO_BUTTON);

        Assert.Equal(1, _telegram.Count_HeldEdits_Of(RECEIPT_ID));
        Assert.True(_receipts.Has_PendingEdit(RECEIPT_ID));

        // Before the door: the drain finds nothing due and calls nothing.
        await Drain_Async();
        Assert.Equal(("✓ ⏸ holding · 1 message", "▶ GO"), _telegram.Current_Of_OrNull(RECEIPT_ID));

        await Task.Delay(PAST_THE_GAP);
        await Drain_Async();

        Assert.Equal(("✓ ⏸ holding · 2 messages", "▶ GO"), _telegram.Current_Of_OrNull(RECEIPT_ID));
        Assert.False(_receipts.Has_PendingEdit(RECEIPT_ID));
        Assert.True(_log.Has_Info_Containing($"Receipt edit of message {RECEIPT_ID} landed after waiting for its door"), _log.Dump());
    }

    /// <summary>
    /// LAST WRITER WINS: three texts inside one gap, and only the first and the last ever reach the phone.
    /// The middle one is replaced in the slot before the door opens — a detached retry holding its own copy
    /// would have drawn it over the newer one.
    /// </summary>
    [Fact]
    public async Task OnlyTheNewestOfSeveralHeldTexts_EverLands()
    {
        await Write_Async("✓", buttons: null);
        await Write_Async("✓✓", buttons: null);
        await Write_Async("✓✓  ·  Sup: turn ended — free now, they are reading this", buttons: null);

        // The third was not even attempted: the door was already known to be shut.
        Assert.Equal(1, _telegram.Count_HeldEdits_Of(RECEIPT_ID));

        await Task.Delay(PAST_THE_GAP);
        await Drain_Async();

        Assert.Equal(
            ["✓", "✓✓  ·  Sup: turn ended — free now, they are reading this"],
            _telegram.TextEdits.Where(edit => edit.MessageId == RECEIPT_ID).Select(edit => edit.Text).ToList());
    }

    /// <summary>
    /// TELEGRAM'S OWN "SLOW DOWN" IS WAITED OUT TOO: a 429 on the first attempt is not rethrown to the writer,
    /// and the edit lands after the wait Telegram named.
    /// </summary>
    [Fact]
    public async Task ARateLimitedEdit_IsWaitedOut_NotRethrown()
    {
        _telegram.Rate_Limit_Edits_Of(RECEIPT_ID, retryAfterSeconds: 1, failFirstAttempts: 1);

        await Write_Async("✓✓", buttons: null);

        Assert.Empty(_telegram.TextEdits);

        // Past the gap as well as the one second Telegram asked for: the first attempt reserved the message's
        // slot before the wire refused it, so the retry meets the door first.
        await Task.Delay(PAST_THE_GAP);
        await Drain_Async();

        Assert.Equal(("✓✓", ""), _telegram.Current_Of_OrNull(RECEIPT_ID));
        Assert.Equal(2, _telegram.Count_EditAttempts_Of(RECEIPT_ID));
    }

    /// <summary>
    /// A REFUSAL NO WAIT WILL FIX IS THE WRITER'S. Telegram said no (the message is gone, a 400): the writer
    /// gets the failure exactly as before — the delivery receipt's fallback and the narration's "is the
    /// message gone" classification depend on it — and nothing is left in the slot to be retried into the
    /// same refusal.
    /// </summary>
    [Fact]
    public async Task ARefusalNoWaitWillFix_IsRethrownToTheWriter_AndNothingIsLeftStaged()
    {
        _telegram.Refuse_Edits_Of(RECEIPT_ID);

        var refusal = await Assert.ThrowsAsync<Exception>(() => Write_Async("✓✓", buttons: null));

        Assert.Contains("scripted edit refusal", refusal.Message, StringComparison.Ordinal);
        Assert.False(_receipts.Has_PendingEdit(RECEIPT_ID));
    }

    /// <summary>
    /// A STAGED EDIT REFUSED AT THE DOOR IS GIVEN UP WITH ONE WARNING, and the drain never throws for it: it
    /// runs inside the mirror tick, and a refusal must cost that edit, not the tick.
    /// </summary>
    [Fact]
    public async Task AStagedEditRefusedAtItsDoor_IsGivenUpWithOneWarning()
    {
        await Write_Async("✓ ⏸ holding · 1 message", GO_BUTTON);
        await Write_Async("✓✓", buttons: null);

        _telegram.Refuse_Edits_Of(RECEIPT_ID);

        await Task.Delay(PAST_THE_GAP);
        await Drain_Async();
        await Drain_Async();

        Assert.False(_receipts.Has_PendingEdit(RECEIPT_ID));
        Assert.Equal(1, _log.Dump().Split(Environment.NewLine).Count(line => line.StartsWith("WARN", StringComparison.Ordinal) && line.Contains("given up", StringComparison.Ordinal)));
    }

    Task Write_Async(string text, IReadOnlyList<(string Data, string Label)>? buttons)
    {
        return ReceiptEdit_Sender.Write_Async(_receipts, _telegram, SCOPE, RECEIPT_ID, text, buttons, CancellationToken.None);
    }

    Task Drain_Async()
    {
        return ReceiptEdit_Sender.Drain_Async(_receipts, _telegram, _log, CancellationToken.None);
    }
}
