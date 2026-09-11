using AIOrchestratorCoreLib.Telegram.TelegramApiClient;

namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// What to do after an OWNER-FACING edit was refused — retry after Telegram's own wait, or give up.
///
/// <para>
/// WHY THIS EXISTS. Measured on the VPS, 2026-09-10 21:11:40: the owner tapped an option, the
/// rewrite of the tapped message ("✅ …", keyboard gone) got <c>429 retry after 24</c>, the
/// fallback that removes the keyboard got the same 429, and NEITHER was tried again — the phone kept
/// the old text and a live keyboard on a question already answered. The client's inline retry covers
/// only a <c>retry_after</c> of two seconds or less (a burst), and every 429 this system sees on an
/// edit carries 20–34 s. So the decision of whether to wait that long is the caller's, and it
/// depends on what the edit IS: a status line can wait for the next tick; a tap's rewrite is the one
/// thing the owner is looking at, and it deserves the wait (owner decision, 2026-09-10: up to about a
/// minute, in the background, then give up).
/// </para>
/// <para>
/// PURE, so the schedule can be tested without a clock or a network: the caller carries the attempt
/// number and sleeps for what this returns.
/// </para>
/// </summary>
public static class RateLimitedRetry_Policy
{
    /// <summary>Attempts in total, the first one included. Three covers a 429 that is a wave, not a wall.</summary>
    public const int MAX_ATTEMPTS = 3;

    /// <summary>
    /// The most one wait may last. Telegram's <c>retry_after</c> on an edit was measured at 20–34 s;
    /// a value above this is a wall, and a tap's rewrite is not worth parking a task on for minutes.
    /// Two waits at the cap plus the attempts themselves stay inside the owner's "about a minute".
    /// </summary>
    public const int MAX_WAIT_SECONDS = 30;

    /// <summary>A 429 with no <c>retry_after</c> still has to cost something, or the retry is immediate.</summary>
    public const int DEFAULT_WAIT_SECONDS = 5;

    /// <summary>
    /// How long to wait before attempt <paramref name="attemptsMade"/> + 1, or null to stop. FOR A
    /// FAILURE THAT CARRIES ITS OWN RELATIVE WAIT — today only a 429's <c>retry_after</c>, which is
    /// already a duration and needs no clock to turn into one. Null for anything that is not a rate
    /// limit: a 400 will refuse the identical request again, a 5xx is not a wait but an outage, and
    /// neither is this policy's to retry.
    ///
    /// <para>
    /// A <see cref="TelegramHeldException"/> carries an ABSOLUTE deadline instead
    /// (<see cref="TelegramHeldException.NotBeforeUtc"/>), and turning a deadline into a wait needs
    /// "now" — so that case is handled only by the <see cref="Wait_BeforeNextAttempt_OrNull(Exception,int,DateTime)"/>
    /// overload below. This overload treats a held exception the same as anything else that is not a
    /// 429: null. Call the clocked overload wherever a held exception can reach this policy.
    /// </para>
    /// </summary>
    public static TimeSpan? Wait_BeforeNextAttempt_OrNull(Exception failure, int attemptsMade)
    {
        if (attemptsMade >= MAX_ATTEMPTS)
            return null;

        if (failure is not TelegramApiException { StatusCode: 429 } rateLimited)
            return null;

        var seconds = rateLimited.RetryAfterSeconds ?? DEFAULT_WAIT_SECONDS;

        if (seconds < 1)
            seconds = DEFAULT_WAIT_SECONDS;

        return TimeSpan.FromSeconds(Math.Min(seconds, MAX_WAIT_SECONDS));
    }

    /// <summary>
    /// The clocked overload — the one that also understands a <see cref="TelegramHeldException"/>.
    ///
    /// <para>
    /// A HELD WINDOW SPEEDS THE TAP UP, IT DOES NOT CANCEL IT. The door is already shut for a reason
    /// this policy trusts (<see cref="OutboundCooldowns"/> — Telegram's own <c>retry_after</c>,
    /// already recorded by whichever surface hit it first), so the owner's tap is scheduled for
    /// exactly <see cref="TelegramHeldException.NotBeforeUtc"/> rather than trying immediately and
    /// drawing a second, redundant 429 on the same window.
    /// </para>
    /// <para>
    /// A DEADLINE ALREADY PAST IS ZERO WAIT, NOT NO WAIT: the door has simply opened between the
    /// exception being thrown and this being asked, which is routine under concurrent callers, and
    /// it is still a legitimate attempt against the counted <see cref="MAX_ATTEMPTS"/> — returning
    /// null here would read as "give up" to every caller of this policy, and the correct read is
    /// "go now".
    /// </para>
    /// <para>
    /// A HELD WINDOW IS CLAMPED, THEN ATTEMPTED — the same choice the 429 branch already makes, for
    /// the same reason: the existing doc on this type says a tap's rewrite is not worth parking a
    /// task on for minutes, and the alternative (give up because the wait is long) is exactly the
    /// state 2026-09-10 21:11:40 measured — old text and a live keyboard on an answered question. An
    /// attempt after 30 s costs one more HTTP call; giving up costs the owner a wrong screen.
    /// </para>
    /// </summary>
    public static TimeSpan? Wait_BeforeNextAttempt_OrNull(Exception failure, int attemptsMade, DateTime nowUtc)
    {
        if (failure is TelegramHeldException held)
        {
            if (attemptsMade >= MAX_ATTEMPTS)
                return null;

            var wait = held.NotBeforeUtc - nowUtc;

            if (wait <= TimeSpan.Zero)
                return TimeSpan.Zero;

            return wait > TimeSpan.FromSeconds(MAX_WAIT_SECONDS)
                ? TimeSpan.FromSeconds(MAX_WAIT_SECONDS)
                : wait;
        }

        return Wait_BeforeNextAttempt_OrNull(failure, attemptsMade);
    }
}
