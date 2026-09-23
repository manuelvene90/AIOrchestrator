namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// THE BUTTONS UNDER A RECEIPT — the ✓ tick, and the "⏸ holding" message a WAIT turns it into (plan 03
/// Task 5). Every receipt-side hold button in the engine is drawn here, and each one is drawn ONLY when
/// the toggle's single placement (<see cref="HoldTogglePlacement_Resolver"/>) is the receipt.
///
/// <para>
/// FOUR SITES DREW A HOLD BUTTON ON A RECEIPT, each with its own literal: the ✓ tick (⏸ Wait), the
/// typed WAIT's acknowledgement (▶ GO), that acknowledgement counting up (▶ GO again), and the rewrite
/// after a tap on either. With the toggle on PULSE every one of them was a second copy of it — the
/// fork's ✓ fallback still carried ⏸ Wait while PULSE carried ⏸ Wait too, and a typed WAIT put ▶ GO on
/// the acknowledgement while PULSE counted up its own ▶ GO. One toggle in two places is CLAUDE.md
/// decision 12, and the owner ruled the same way on 2026-09-10; asking one builder at all four sites is
/// what keeps "never both" a property of the code rather than of four call sites remembering it.
/// </para>
/// <para>
/// AN EMPTY RESULT IS A RECEIPT WITH NO KEYBOARD, not a failure: the client sends it with no
/// <c>reply_markup</c> at all, and on an edit that absence removes whatever keyboard the message had.
/// </para>
/// </summary>
public static class ReceiptButtons_Builder
{
    /// <summary>
    /// The ✓ tick's buttons: ⏸ Wait when the toggle lives on the receipt, nothing when it lives on PULSE.
    /// A tick is the one receipt that exists in the ordinary case, so a later receipt-side button that is
    /// NOT the hold toggle joins this list — and the placement stays decided by the argument, never
    /// re-decided by the button that joins.
    /// </summary>
    public static IReadOnlyList<(string Data, string Label)> Build_ForTick(long? messageThreadId, bool holdToggleOnTheBar)
    {
        return Build_ForHoldReceipt(HoldButtonActions.Hold, messageThreadId, holdToggleOnTheBar);
    }

    /// <summary>
    /// A receipt's hold-family button — <paramref name="offered"/> is the action a tap would take (⏸ Wait
    /// offers a hold, ▶ GO offers the release) — or nothing when the toggle lives on PULSE. The payload is
    /// <see cref="HoldButton_Data"/>'s, the same family the bar's toggle uses, so the tap handler cannot
    /// tell the two homes apart and does not need to.
    /// </summary>
    public static IReadOnlyList<(string Data, string Label)> Build_ForHoldReceipt(HoldButtonActions offered, long? messageThreadId, bool holdToggleOnTheBar)
    {
        if (holdToggleOnTheBar)
            return [];

        var label = offered == HoldButtonActions.Hold ? HoldButton_Data.HOLD_LABEL : HoldButton_Data.GO_LABEL;

        return [(HoldButton_Data.Build(offered, messageThreadId), label)];
    }
}
