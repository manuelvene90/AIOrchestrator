using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// AN OWNER-FACING EDIT WAITS FOR TELEGRAM'S OWN NUMBER, THEN TRIES AGAIN — bounded.
///
/// <para>
/// Measured 2026-09-10 21:11:40 on the VPS: the rewrite of a tapped question got
/// <c>429 retry after 24</c> and was never retried, so the phone kept a live keyboard on a question
/// already answered. The policy below is the schedule that rewrite now follows.
/// </para>
/// </summary>
public class RateLimitedRetryPolicyTests
{
    static TelegramApiException RateLimited(int? retryAfter) => new(429, "Too Many Requests", retryAfter);

    [Fact]
    public void ARateLimit_IsWaitedOut_ForTelegramsOwnNumber()
    {
        Assert.Equal(TimeSpan.FromSeconds(24), RateLimitedRetry_Policy.Wait_BeforeNextAttempt_OrNull(RateLimited(24), attemptsMade: 1));
    }

    [Fact]
    public void AWaitAboveTheCap_IsClampedToIt()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(RateLimitedRetry_Policy.MAX_WAIT_SECONDS),
            RateLimitedRetry_Policy.Wait_BeforeNextAttempt_OrNull(RateLimited(600), attemptsMade: 1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-3)]
    public void ARateLimitWithNoUsableNumber_StillCostsAWait(int? retryAfter)
    {
        Assert.Equal(
            TimeSpan.FromSeconds(RateLimitedRetry_Policy.DEFAULT_WAIT_SECONDS),
            RateLimitedRetry_Policy.Wait_BeforeNextAttempt_OrNull(RateLimited(retryAfter), attemptsMade: 1));
    }

    /// <summary>The last allowed attempt is the cap itself: after it, stop.</summary>
    [Fact]
    public void AfterTheLastAttempt_ItGivesUp()
    {
        Assert.NotNull(RateLimitedRetry_Policy.Wait_BeforeNextAttempt_OrNull(RateLimited(5), attemptsMade: RateLimitedRetry_Policy.MAX_ATTEMPTS - 1));
        Assert.Null(RateLimitedRetry_Policy.Wait_BeforeNextAttempt_OrNull(RateLimited(5), attemptsMade: RateLimitedRetry_Policy.MAX_ATTEMPTS));
    }

    /// <summary>A 400 refuses the identical request again; a 5xx is an outage; neither is a wait.</summary>
    [Theory]
    [InlineData(400)]
    [InlineData(500)]
    [InlineData(502)]
    public void AnythingButARateLimit_IsNotRetried(int status)
    {
        Assert.Null(RateLimitedRetry_Policy.Wait_BeforeNextAttempt_OrNull(new TelegramApiException(status, "no", null), attemptsMade: 1));
        Assert.Null(RateLimitedRetry_Policy.Wait_BeforeNextAttempt_OrNull(new InvalidOperationException("no"), attemptsMade: 1));
    }

    /// <summary>The owner's bound: "up to about a minute". Two waits at the cap plus the attempts stay inside it.</summary>
    [Fact]
    public void TheWholeSchedule_StaysInsideAboutAMinute()
    {
        var worstCase = (RateLimitedRetry_Policy.MAX_ATTEMPTS - 1) * RateLimitedRetry_Policy.MAX_WAIT_SECONDS;

        Assert.True(worstCase <= 60, $"worst case waits {worstCase} s");
    }

    // --- TelegramHeldException — the clocked overload ------------------------------------------

    static readonly DateTime Now = new(2026, 9, 10, 21, 11, 40, DateTimeKind.Utc);

    /// <summary>A held window still open schedules the retry for EXACTLY the deadline Telegram gave.</summary>
    [Fact]
    public void AHeldWindow_IsScheduled_ForExactlyItsDeadline()
    {
        var held = new TelegramHeldException("msg:123", Now.AddSeconds(24));

        Assert.Equal(
            TimeSpan.FromSeconds(24),
            RateLimitedRetry_Policy.Wait_BeforeNextAttempt_OrNull(held, attemptsMade: 1, Now));
    }

    /// <summary>A deadline already in the past is a legitimate attempt now — zero wait, not "give up".</summary>
    [Fact]
    public void AHeldWindow_AlreadyPast_IsZeroWait_NotGivingUp()
    {
        var held = new TelegramHeldException("msg:123", Now.AddSeconds(-5));

        Assert.Equal(
            TimeSpan.Zero,
            RateLimitedRetry_Policy.Wait_BeforeNextAttempt_OrNull(held, attemptsMade: 1, Now));
    }

    /// <summary>Boundary: a deadline exactly equal to now is the door just having opened — zero wait.</summary>
    [Fact]
    public void AHeldWindow_DeadlineExactlyNow_IsZeroWait()
    {
        var held = new TelegramHeldException("msg:123", Now);

        Assert.Equal(
            TimeSpan.Zero,
            RateLimitedRetry_Policy.Wait_BeforeNextAttempt_OrNull(held, attemptsMade: 1, Now));
    }

    /// <summary>
    /// A held window past the cap is clamped and attempted anyway — never given up on. Giving up is
    /// the exact state measured 2026-09-10 21:11:40: old text and a live keyboard on an answered
    /// question.
    /// </summary>
    [Fact]
    public void AHeldWindow_PastTheCap_IsClampedThenAttempted()
    {
        var held = new TelegramHeldException("msg:123", Now.AddMinutes(5));

        Assert.Equal(
            TimeSpan.FromSeconds(RateLimitedRetry_Policy.MAX_WAIT_SECONDS),
            RateLimitedRetry_Policy.Wait_BeforeNextAttempt_OrNull(held, attemptsMade: 1, Now));
    }

    /// <summary>Still respects MAX_ATTEMPTS: a held window does not buy extra tries.</summary>
    [Fact]
    public void AHeldWindow_AfterTheLastAttempt_ItGivesUp()
    {
        var held = new TelegramHeldException("msg:123", Now.AddSeconds(5));

        Assert.NotNull(RateLimitedRetry_Policy.Wait_BeforeNextAttempt_OrNull(held, RateLimitedRetry_Policy.MAX_ATTEMPTS - 1, Now));
        Assert.Null(RateLimitedRetry_Policy.Wait_BeforeNextAttempt_OrNull(held, RateLimitedRetry_Policy.MAX_ATTEMPTS, Now));
    }

    /// <summary>The clocked overload still handles a 429 exactly like the pure one — the clock is unused there.</summary>
    [Fact]
    public void TheClockedOverload_StillHandlesA429_LikeTheOriginal()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(24),
            RateLimitedRetry_Policy.Wait_BeforeNextAttempt_OrNull(RateLimited(24), attemptsMade: 1, Now));
    }

    /// <summary>Neither a held window nor a rate limit — the clocked overload still gives up too.</summary>
    [Fact]
    public void TheClockedOverload_StillGivesUp_ForAnythingElse()
    {
        Assert.Null(RateLimitedRetry_Policy.Wait_BeforeNextAttempt_OrNull(new InvalidOperationException("no"), attemptsMade: 1, Now));
    }
}
