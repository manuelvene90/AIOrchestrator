namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// Whether Telegram has already told us to wait for this target, and until when — the arithmetic,
/// PURE, so a 34-second window can be tested in a millisecond.
///
/// <para>
/// WHY THIS EXISTS. Telegram answers a rate limit with a WAIT, and until now that answer was known
/// to exactly one caller: the one that received it. Everything else kept ringing the same bell.
/// Measured on the VPS 2026-09-10 (build 481efc9, one hour to 22:38): 388 HTTP 429, of which 376
/// were two topic status lines re-attempted at the 2 s tick rate against a <c>retry_after</c> of
/// 20–34 s. The journal shows the countdown — 34, 31, 29, 27, 25, 22, 20 — which is ONE window
/// being re-reported once per tick, not 388 separate violations. The app never waited; it simply
/// kept asking, and every ask was a line in the log.
/// </para>
/// <para>
/// THEIR NUMBER, NOT OURS. <see cref="TokenBucket_Gate"/> already says it — a bucket is an estimate
/// of a limit we cannot see, a 429 carries the limit's own answer — and the same clamp is reused
/// here rather than a second one, so the process has ONE ceiling on how long Telegram may park a
/// surface.
/// </para>
/// <para>
/// NOT A RETRY SCHEDULE. <see cref="RateLimitedRetry_Policy"/> decides whether ONE caller should
/// wait and try again; this decides whether ANY caller should attempt at all. The first is about a
/// call's worth, the second about a door being shut.
/// </para>
/// </summary>
public static class OutboundCooldown_Gate
{
    /// <summary>
    /// A rate limit with no <c>retry_after</c> still has to cost something, or the next attempt is
    /// immediate and lands on the same wall. Matches the client's own inline default so the two
    /// cannot disagree about what "no advice" is worth.
    /// </summary>
    public const int DEFAULT_WAIT_SECONDS = 5;

    /// <summary>
    /// When the door may be tried again, from Telegram's own <c>retry_after</c>. Clamped by
    /// <see cref="TokenBucket_Gate.MAXIMUM_HONOURED_RETRY_AFTER_SECONDS"/>, so a malformed or
    /// hostile payload cannot park a surface for a day.
    /// </summary>
    public static DateTime Deadline_From_RetryAfter(int? retryAfterSeconds, DateTime nowUtc)
    {
        var wait = TokenBucket_Gate.Read_RetryAfter(retryAfterSeconds);

        if (wait <= TimeSpan.Zero)
            wait = TimeSpan.FromSeconds(DEFAULT_WAIT_SECONDS);

        return nowUtc + wait;
    }

    /// <summary>
    /// Whether the door is still shut. OPEN AT THE DEADLINE ITSELF, which is the convention
    /// <see cref="TelegramAttempt_Gate.Is_AttemptDue"/> already uses — two gates disagreeing about
    /// the edge is how a retry lands one tick late for ever.
    /// </summary>
    public static bool Is_Held(DateTime? deadlineUtc, DateTime nowUtc)
    {
        return deadlineUtc != null && nowUtc < deadlineUtc.Value;
    }
}

/// <summary>
/// The door a cooldown belongs to. TWO NAMESPACES THAT MUST NOT COLLIDE: Telegram limits edits of
/// one MESSAGE far more tightly than calls to a CHAT (<see cref="TokenBucket_Gate"/> documents the
/// measurement), so the two are different doors even when the numbers happen to match.
/// </summary>
public static class OutboundCooldown_Keys
{
    public static string For_Message(long messageId) => $"msg:{messageId}";

    /// <summary>
    /// Unused by the per-target slice and here for the queue that follows it: a send is refused for
    /// the chat, not for a message that does not exist yet.
    /// </summary>
    public static string For_Chat(long chatId) => $"chat:{chatId}";
}
