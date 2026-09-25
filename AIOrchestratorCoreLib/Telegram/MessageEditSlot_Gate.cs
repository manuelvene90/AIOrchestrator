using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using AIOrchestratorCoreLib.Telegram.TelegramSendBudget;

namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// THE PER-MESSAGE EDIT SLOT, TURNED INTO WHAT A CALLER SEES — a reserved slot, or a
/// <see cref="TelegramHeldException"/> naming the message's door and when it opens.
///
/// <para>
/// ONE PLACE, SO THE HARNESS CANNOT DRIFT FROM THE CLIENT (plan 03 Task 6c, 2026-09-23). The real
/// client runs this before every edit it makes (<c>Hold_UnlessThisMessageMayBeEdited</c>), and the
/// engine tests that prove a held ✓✓ is retried wrap their fake in the same call — so what they see
/// held is what the owner's phone would see held, not a copy of the rule written out again in a test
/// project (CLAUDE.md decision 12).
/// </para>
/// <para>
/// IT DOES NOT SLEEP, like the budget it reads: the slot is reserved on success and left alone on a
/// refusal, and the caller decides whether its edit is worth waiting for.
/// </para>
/// </summary>
public static class MessageEditSlot_Gate
{
    public static void Reserve_OrThrowHeld(ITelegramSendBudget budget, long messageId, DateTime nowUtc)
    {
        var owed = budget.Reserve_MessageEdit(messageId, nowUtc);

        if (owed > TimeSpan.Zero)
            throw new TelegramHeldException(OutboundCooldown_Keys.For_Message(messageId), nowUtc + owed);
    }
}
