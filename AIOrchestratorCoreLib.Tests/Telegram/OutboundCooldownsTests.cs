using Xunit;
using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// THE NOTE ON THE DOOR. Telegram answers a rate limit with a wait, and until now that answer was
/// known to exactly one caller — the one that received it. Everything else kept ringing the same
/// bell: measured on the VPS 2026-09-10, 388 HTTP 429 in one hour, of which 376 were two status
/// lines re-attempted at the 2 s tick rate against a wait of 20–34 s.
///
/// <para>
/// These pin the arithmetic and the book-keeping, with no clock and no network: every method takes
/// <c>nowUtc</c>, which is the only reason a 34-second window can be tested in a millisecond.
/// </para>
/// </summary>
public class OutboundCooldownsTests
{
    static readonly DateTime NOW = new(2026, 9, 10, 21, 11, 31, DateTimeKind.Utc);

    // ── the pure arithmetic ────────────────────────────────────────────────────────────────────

    [Fact]
    public void TelegramsOwnWaitBecomesTheDeadline()
    {
        var deadline = OutboundCooldown_Gate.Deadline_From_RetryAfter(retryAfterSeconds: 24, NOW);

        Assert.Equal(NOW.AddSeconds(24), deadline);
    }

    [Fact]
    public void A429WithNoWaitStillCostsSomething()
    {
        // Otherwise the next attempt is immediate and lands on the same wall — the whole defect.
        var deadline = OutboundCooldown_Gate.Deadline_From_RetryAfter(retryAfterSeconds: null, NOW);

        Assert.True(deadline > NOW, "a rate limit with no retry_after must still open a window");
    }

    [Fact]
    public void AnAbsurdWaitIsClampedRatherThanHonoured()
    {
        // A malformed or hostile payload must not park a surface for a day. The clamp is the one
        // TokenBucket_Gate already applies, so there is one ceiling in the process, not two.
        var deadline = OutboundCooldown_Gate.Deadline_From_RetryAfter(retryAfterSeconds: 86_400, NOW);

        Assert.Equal(NOW.AddSeconds(TokenBucket_Gate.MAXIMUM_HONOURED_RETRY_AFTER_SECONDS), deadline);
    }

    [Fact]
    public void AtTheDeadlineItselfTheDoorIsOpenAgain()
    {
        // The same boundary convention TelegramAttempt_Gate already uses: due AT the deadline, not
        // after it. Two gates disagreeing about the edge is how a retry lands one tick late for ever.
        Assert.True(OutboundCooldown_Gate.Is_Held(NOW.AddSeconds(24), NOW));
        Assert.False(OutboundCooldown_Gate.Is_Held(NOW.AddSeconds(24), NOW.AddSeconds(24)));
    }

    [Fact]
    public void NoDeadlineIsNotHeld()
    {
        Assert.False(OutboundCooldown_Gate.Is_Held(deadlineUtc: null, NOW));
    }

    // ── the book-keeping ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void ARefusedTargetIsHeldUntilTelegramsOwnDeadline()
    {
        var cooldowns = new OutboundCooldowns();
        var target = OutboundCooldown_Keys.For_Message(2306);

        cooldowns.Note_RateLimited(target, retryAfterSeconds: 24, NOW);

        Assert.True(cooldowns.Is_Held(target, NOW.AddSeconds(2), out var until));
        Assert.Equal(NOW.AddSeconds(24), until);
        Assert.False(cooldowns.Is_Held(target, NOW.AddSeconds(24), out _));
    }

    [Fact]
    public void ONLY_THE_REFUSED_TARGET_IS_HELD()
    {
        // THE CORE OF THIS SLICE, and a deliberate narrowing of the design doc. A chat-wide hold
        // would be defensible — python-telegram-bot halts every request on a 429 — but priority
        // bands do not exist yet, so a chat-wide hold would put the owner's tap behind a status
        // line's punishment. That IS the inversion this work exists to remove. Per-target until the
        // bands arrive with the queue.
        var cooldowns = new OutboundCooldowns();

        cooldowns.Note_RateLimited(OutboundCooldown_Keys.For_Message(2306), retryAfterSeconds: 24, NOW);

        Assert.False(cooldowns.Is_Held(OutboundCooldown_Keys.For_Message(9999), NOW, out _));
    }

    [Fact]
    public void ALONGER_WAIT_EXTENDS_THE_WINDOW_A_SHORTER_ONE_DOES_NOT_SHORTEN_IT()
    {
        // Two surfaces can be refused on the same message within one window (the rewrite after a
        // tap and the keyboard-removal fallback both got 429 at 21:11:40). The later, smaller
        // number is Telegram counting down the SAME window — honouring it would re-open the door
        // early, which is exactly how the retry storm sustained itself.
        var cooldowns = new OutboundCooldowns();
        var target = OutboundCooldown_Keys.For_Message(2306);

        cooldowns.Note_RateLimited(target, retryAfterSeconds: 30, NOW);
        cooldowns.Note_RateLimited(target, retryAfterSeconds: 10, NOW.AddSeconds(2));

        Assert.True(cooldowns.Is_Held(target, NOW.AddSeconds(20), out var until));
        Assert.Equal(NOW.AddSeconds(30), until);
    }

    [Fact]
    public void THE_WINDOW_IS_ANNOUNCED_ONCE_NOT_ONCE_PER_ATTEMPT()
    {
        // 388 log lines an hour said the same thing 194 times per topic. A 429 storm must not
        // become a log storm, so the caller is told to speak only when a window OPENS.
        var cooldowns = new OutboundCooldowns();
        var target = OutboundCooldown_Keys.For_Message(2306);

        Assert.True(cooldowns.Note_RateLimited(target, retryAfterSeconds: 30, NOW));
        Assert.False(cooldowns.Note_RateLimited(target, retryAfterSeconds: 28, NOW.AddSeconds(2)));
        Assert.False(cooldowns.Note_RateLimited(target, retryAfterSeconds: 26, NOW.AddSeconds(4)));

        // A window that has expired and re-opens is news again.
        Assert.True(cooldowns.Note_RateLimited(target, retryAfterSeconds: 20, NOW.AddSeconds(31)));
    }

    [Fact]
    public void EXPIRED_WINDOWS_DO_NOT_ACCUMULATE()
    {
        // One entry per message edited, and a long-lived daemon edits a lot of them. Bounded by the
        // windows still open, not by everything it has ever been refused.
        var cooldowns = new OutboundCooldowns();

        for (var messageId = 1; messageId <= 500; messageId++)
            cooldowns.Note_RateLimited(OutboundCooldown_Keys.For_Message(messageId), retryAfterSeconds: 30, NOW);

        Assert.Equal(500, cooldowns.Count_OpenWindows(NOW));
        Assert.Equal(0, cooldowns.Count_OpenWindows(NOW.AddSeconds(31)));
    }

    [Fact]
    public void A_MESSAGE_KEY_AND_A_CHAT_KEY_ARE_DIFFERENT_DOORS()
    {
        // The chat key is unused by this slice and exists for the queue; pin now that it cannot
        // collide with a message id, because the two namespaces sharing one dictionary is the kind
        // of accident that reads as "the cooldown randomly holds sends".
        Assert.NotEqual(OutboundCooldown_Keys.For_Message(42), OutboundCooldown_Keys.For_Chat(42));
    }
}
