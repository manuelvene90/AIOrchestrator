namespace AIOrchestratorCoreLib.Telegram.TelegramApiClient;

/// <summary>
/// NOT NOW — as distinct from NO. The call was never made, because Telegram had already told us to
/// wait for this exact target and the wait has not expired.
///
/// <para>
/// WHY IT IS ITS OWN TYPE. Every one of the ~40 Telegram call sites catches broadly and logs a
/// warning. If a held call travelled the same path as a refusal, a 429 storm would simply become a
/// log storm — 388 lines an hour saying the same thing — and the classifiers would read a shut door
/// as an outcome they could not learn (<see cref="TelegramAttempt_Gate"/>'s
/// <c>OutcomeUnknown</c>), which is the reading that once recorded a topic name as applied for ever.
/// A distinct type lets a caller tell the two apart without parsing a message string.
/// </para>
/// <para>
/// IT CARRIES THE DEADLINE, so a caller that is WORTH the wait can wait exactly as long as Telegram
/// asked rather than guessing. That is the owner's tap: <see cref="RateLimitedRetry_Policy"/> reads
/// <see cref="NotBeforeUtc"/> and schedules the next attempt on it, so the note on the door speeds
/// the rewrite up instead of cancelling it — the door is shut for the surfaces that can come back
/// next tick, not for the one thing the owner is looking at.
/// </para>
/// <para>
/// It is deliberately NOT a <see cref="TelegramApiException"/>: there was no HTTP status, no body
/// and no wire call. Inventing a 429 we did not receive would put a violation in the log that never
/// happened, and the count of 429s is the measurement this whole change is judged by.
/// </para>
/// </summary>
public sealed class TelegramHeldException : Exception
{
    public TelegramHeldException(string target, DateTime notBeforeUtc)
        : base($"Telegram call to '{target}' was NOT attempted: rate-limit window held until {notBeforeUtc:O}")
    {
        Target = target;
        NotBeforeUtc = notBeforeUtc;
    }

    /// <summary>The door that is shut — a message or a chat (<see cref="OutboundCooldown_Keys"/>).</summary>
    public string Target { get; }

    /// <summary>When the target may be attempted again, as Telegram itself stated it.</summary>
    public DateTime NotBeforeUtc { get; }
}
