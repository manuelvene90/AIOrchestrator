using System.Runtime.ExceptionServices;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;

namespace AIOrchestratorCoreLib.Bridge.ReceiptRegistry;

/// <summary>
/// THE ONE WAY A RECEIPT MESSAGE IS EDITED — through its slot in <see cref="IReceiptRegistry"/>, so a
/// held edit waits for the message's door instead of being dropped (plan 03 Task 6c, ruling R18).
///
/// <para>
/// THE INCIDENT IT CLOSES. Telegram allows one edit of a message per
/// <see cref="TokenBucket_Gate.MINIMUM_GAP_BETWEEN_EDITS_OF_ONE_MESSAGE"/>, and the client refuses the
/// second BEFORE the call (<see cref="TelegramHeldException"/>). On classic the owner's flow is ⏸ Wait then
/// ▶ GO: the Wait rewrites the ✓ (its first edit), and the ✓✓ at delivery is its second. The delivery
/// receipt caught the held exception, logged it, and never tried again — the ✓'s id was already out of the
/// registry, so no later edit found the message either. It read "⏸ holding" for ever, on a message that
/// had been delivered (review of Tasks 6/6b, 2026-09-23).
/// </para>
/// <para>
/// WRITE TRIES NOW WHEN IT MAY, STAGES WHEN IT MAY NOT. The first attempt is inline, so an edit whose door
/// is open costs what it always did. A held door or a 429 is NOT a failure: the newest text stays in the
/// slot and <see cref="Drain_Async"/>, run on the mirror tick, lands it once the door opens. What the
/// retry policy will not wait for (<see cref="RateLimitedRetry_Policy"/> — a 400, an outage, a timeout)
/// is rethrown to the caller exactly as before, so every writer keeps its own fallback: the delivery
/// receipt its "send a new one", the busy narration its "is the message gone" classification.
/// </para>
/// <para>
/// THE CLOCK IS THE WALL CLOCK, not the engine's injectable one, because the door is: the client stamps
/// and checks the per-message slot against <see cref="DateTime.UtcNow"/>, and a deadline compared against
/// a different clock would open early or never.
/// </para>
/// </summary>
public static class ReceiptEdit_Sender
{
    /// <summary>
    /// Stages <paramref name="text"/> (and <paramref name="buttons"/>; null is a plain edit, which removes
    /// the keyboard) as what <paramref name="messageId"/> should show, and sends it now when its door is
    /// open. Returns normally when it landed or is waiting for the door; throws only what no wait will fix.
    /// </summary>
    public static async Task Write_Async(
        IReceiptRegistry receipts,
        ITelegramApiClient client,
        string logScope,
        long messageId,
        string text,
        IReadOnlyList<(string Data, string Label)>? buttons,
        CancellationToken cancellationToken)
    {
        var attempt = receipts.Stage_Edit_OrNull(messageId, text, buttons, logScope, DateTime.UtcNow);

        // An attempt is in flight, or the door is known to be shut: the newest text now waits in the slot,
        // and whichever lands next — that attempt's successor, or the drain — lands THIS text, not an older copy.
        if (attempt == null)
            return;

        var (_, abandonedBecause) = await Attempt_Async(receipts, client, attempt.Value, cancellationToken);

        if (abandonedBecause != null)
            ExceptionDispatchInfo.Throw(abandonedBecause);
    }

    /// <summary>
    /// Lands every staged edit whose door has opened. Never throws for a Telegram failure — a refusal the
    /// policy gives up on is logged once and its slot dropped; only cancellation propagates.
    /// </summary>
    public static async Task Drain_Async(IReceiptRegistry receipts, ITelegramApiClient client, IOrchestrationLog log, CancellationToken cancellationToken)
    {
        foreach (var attempt in receipts.Take_DueEdits(DateTime.UtcNow))
        {
            var (landed, abandonedBecause) = await Attempt_Async(receipts, client, attempt, cancellationToken);

            // ONE LINE PER OUTCOME, NEVER ONE PER REFUSAL. A held attempt is silent — it is "not now", and
            // logging it is the storm TelegramHeldException exists to prevent; what is worth a line is the
            // edit that finally landed late, and the one that was given up on.
            if (landed)
                log.Log_Info(attempt.LogScope, $"Receipt edit of message {attempt.MessageId} landed after waiting for its door (attempt {attempt.AttemptNumber})");
            else if (abandonedBecause != null)
                log.Log_Warning(attempt.LogScope, $"Receipt edit of message {attempt.MessageId} given up after {attempt.AttemptNumber} attempt(s): {abandonedBecause.Message}");
        }
    }

    /// <summary>
    /// One attempt, settled in the registry: landed, deferred (neither value set), or abandoned with the
    /// failure — which a writer rethrows and the drain logs.
    /// </summary>
    static async Task<(bool Landed, Exception? AbandonedBecause)> Attempt_Async(
        IReceiptRegistry receipts,
        ITelegramApiClient client,
        (long MessageId, long Version, string Text, IReadOnlyList<(string Data, string Label)>? Buttons, int AttemptNumber, string LogScope) attempt,
        CancellationToken cancellationToken)
    {
        try
        {
            if (attempt.Buttons == null)
                await client.Edit_MessageText_Async(attempt.MessageId, attempt.Text, cancellationToken);
            else
                await client.Edit_MessageTextWithButtons_Async(attempt.MessageId, attempt.Text, attempt.Buttons, cancellationToken);

            receipts.Settle_EditLanded(attempt.MessageId, attempt.Version);
            return (true, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown: nothing will drain this slot again, and it must not be left marked in flight.
            receipts.Settle_EditAbandoned(attempt.MessageId, attempt.Version);
            throw;
        }
        catch (Exception ex)
        {
            var nowUtc = DateTime.UtcNow;

            // THE CLOCKED OVERLOAD, because a held door carries an ABSOLUTE deadline and only this one reads
            // it — the unclocked one would call a shut door "not a rate limit" and give up, which is the drop
            // this class exists to end.
            var wait = RateLimitedRetry_Policy.Wait_BeforeNextAttempt_OrNull(ex, attempt.AttemptNumber, nowUtc);

            if (wait != null)
            {
                receipts.Settle_EditDeferred(attempt.MessageId, nowUtc + wait.Value);
                return (false, null);
            }

            receipts.Settle_EditAbandoned(attempt.MessageId, attempt.Version);
            return (false, ex);
        }
    }
}
