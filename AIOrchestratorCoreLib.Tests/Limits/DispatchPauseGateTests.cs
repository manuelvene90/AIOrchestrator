using AIOrchestratorCoreLib.Limits;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Limits;

/// <summary>
/// The gate that stops the dispatcher LAUNCHING new sessions once the account is about to run out
/// of allowance — see <see cref="DispatchPause_Gate"/> for why this exists (sixty identical
/// failures instead of one message with a time on it). Every instant here is a fixed
/// <see cref="DateTime"/>, never <c>DateTime.UtcNow</c>: a gate whose own tests are flaky by the
/// clock is not a gate anyone can trust the boundary of.
/// </summary>
public class DispatchPauseGateTests
{
    static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void BelowTheThreshold_NoPauseIsDecided()
    {
        var result = DispatchPause_Gate.Decide_PauseUntil_OrNull(
            currentPercent: 94.9,
            windowResetsAtUtc: null,
            thresholdPercent: 95,
            nowUtc: Now);

        Assert.Null(result);
    }

    /// <summary>
    /// The threshold is inclusive: at exactly 95% the guard must already be up, not one reading
    /// later. A gate that waits to be strictly OVER its own number spends one more turn's worth of
    /// allowance before it does anything.
    /// </summary>
    [Fact]
    public void AtExactlyTheThreshold_ItPauses()
    {
        var result = DispatchPause_Gate.Decide_PauseUntil_OrNull(
            currentPercent: 95.0,
            windowResetsAtUtc: null,
            thresholdPercent: 95,
            nowUtc: Now);

        Assert.NotNull(result);
    }

    [Fact]
    public void ItResumesAtTheWindowsOwnResetTime_WhenThatIsInTheFuture()
    {
        var resetsAt = Now.AddHours(3);

        var result = DispatchPause_Gate.Decide_PauseUntil_OrNull(
            currentPercent: 99,
            windowResetsAtUtc: resetsAt,
            thresholdPercent: 95,
            nowUtc: Now);

        Assert.Equal(resetsAt, result);
    }

    /// <summary>
    /// A <c>resets_at</c> in the past is a STALE probe reading, not a resume time. Using it as one
    /// would lift the pause on the very tick that set it — the pause would exist for zero seconds,
    /// which to the owner looks exactly like no guard running at all.
    /// </summary>
    [Fact]
    public void AResetsAtInThePast_IsIgnored_AndTheFallbackWindowIsUsedInstead()
    {
        var staleResetsAt = Now.AddMinutes(-5);

        var result = DispatchPause_Gate.Decide_PauseUntil_OrNull(
            currentPercent: 99,
            windowResetsAtUtc: staleResetsAt,
            thresholdPercent: 95,
            nowUtc: Now);

        Assert.Equal(Now.AddMinutes(DispatchPause_Gate.FALLBACK_PAUSE_MINUTES), result);
    }

    /// <summary>
    /// A 429 is the account itself refusing — a fact, not a probe file's opinion of one — so this
    /// path takes no percentage argument at all. There is nothing to threshold against.
    /// </summary>
    [Fact]
    public void ARateLimitRefusal_NeedsNoPercentageAtAll_AndUsesTheWindowsResetTime()
    {
        var resetsAt = Now.AddMinutes(20);

        var result = DispatchPause_Gate.Decide_PauseUntil_ForRateLimit(resetsAt, Now);

        Assert.Equal(resetsAt, result);
    }

    [Fact]
    public void ARateLimitRefusalWithNoResetTime_GetsTheFallbackWindow()
    {
        var result = DispatchPause_Gate.Decide_PauseUntil_ForRateLimit(null, Now);

        Assert.Equal(Now.AddMinutes(DispatchPause_Gate.FALLBACK_PAUSE_MINUTES), result);
    }

    [Fact]
    public void Is_Paused_IsFalse_WhenNothingIsStored()
    {
        Assert.False(DispatchPause_Gate.Is_Paused(null, Now));
    }

    [Fact]
    public void Is_Paused_IsTrue_BeforeTheStoredInstant()
    {
        Assert.True(DispatchPause_Gate.Is_Paused(Now.AddMinutes(1), Now));
    }

    /// <summary>
    /// FALSE at the instant itself, not true. A gate that is still "paused" AT its own due time is
    /// silently longer than the resume time it told the owner — the message says "back at 19:40"
    /// and the guard would still be blocking new work at 19:40:00.
    /// </summary>
    [Fact]
    public void Is_Paused_IsFalse_AtExactlyTheStoredInstant()
    {
        Assert.False(DispatchPause_Gate.Is_Paused(Now, Now));
    }

    [Fact]
    public void Describe_Pause_NamesTheWindow_ThePercentage_AndTheResumeTime()
    {
        var resumeAt = Now.AddHours(2).AddMinutes(15);

        var text = DispatchPause_Gate.Describe_Pause("5-hour", 97.3, resumeAt, Now);

        Assert.Contains("5-hour", text);
        Assert.Contains("97.3", text);
        Assert.Contains(resumeAt.ToString("yyyy-MM-dd HH:mm"), text);
        Assert.Contains("in 2 h 15 min", text);
    }

    /// <summary>
    /// THE DATE IS IN THE MESSAGE. "Resuming at 03:00 UTC" for a pause five days out read as tonight's
    /// on 2026-09-11 and again on 2026-09-18. The instant carries its day and its distance.
    /// </summary>
    [Fact]
    public void Describe_ResumeInstant_CarriesTheDay_AndTheDistance()
    {
        var resumeAt = new DateTime(2026, 9, 21, 3, 0, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc);

        var text = DispatchPause_Gate.Describe_ResumeInstant(resumeAt, now);

        Assert.Equal("2026-09-21 03:00 UTC (in 2 d 17 h)", text);
    }

    [Fact]
    public void Describe_ResumeInstant_UnderAnHour_SaysMinutes_AndNeverZero()
    {
        Assert.Equal($"{Now.AddMinutes(7):yyyy-MM-dd HH:mm} UTC (in 7 min)", DispatchPause_Gate.Describe_ResumeInstant(Now.AddMinutes(7), Now));
        Assert.Equal($"{Now.AddSeconds(20):yyyy-MM-dd HH:mm} UTC (in 1 min)", DispatchPause_Gate.Describe_ResumeInstant(Now.AddSeconds(20), Now));
        Assert.Equal($"{Now.AddMinutes(-5):yyyy-MM-dd HH:mm} UTC (due now)", DispatchPause_Gate.Describe_ResumeInstant(Now.AddMinutes(-5), Now));
    }

    [Fact]
    public void Describe_Resume_CarriesTheReasonItIsGiven()
    {
        var text = DispatchPause_Gate.Describe_Resume("the 5-hour window reset");

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains("the 5-hour window reset", text);
    }
    // ── reconsidering a pause that holds — the brake that could not be lifted (2026-09-11, 2026-09-18)

    /// <summary>
    /// THE INCIDENT, twice: paused for days on a reading from an account that was then swapped; the
    /// live probes read far under the threshold; the pause held anyway because nothing looked again.
    /// A fresh decision of "no pause" while one holds means LIFT.
    /// </summary>
    [Fact]
    public void WhilePaused_ALiveReadingUnderTheThreshold_LiftsThePause()
    {
        var stored = Now.AddDays(4);

        var result = DispatchPause_Gate.Reconsider_WhilePaused(stored, anyReading: true, freshUntilUtc: null);

        Assert.Null(result);
    }

    /// <summary>
    /// The defect the old "never look again" guard was built for, kept by construction: a still-high
    /// reading proposing a LATER instant is discarded, so a pause can never grow past the reset it
    /// was decided on.
    /// </summary>
    [Fact]
    public void WhilePaused_AFreshDecisionThatWouldResolveLater_IsDiscarded()
    {
        var stored = Now.AddMinutes(30);

        var result = DispatchPause_Gate.Reconsider_WhilePaused(stored, anyReading: true, freshUntilUtc: Now.AddHours(5));

        Assert.Equal(stored, result);
    }

    [Fact]
    public void WhilePaused_AFreshDecisionThatWouldResolveEarlier_ShortensThePause()
    {
        var stored = Now.AddDays(4);
        var earlier = Now.AddMinutes(20);

        var result = DispatchPause_Gate.Reconsider_WhilePaused(stored, anyReading: true, freshUntilUtc: earlier);

        Assert.Equal(earlier, result);
    }

    [Fact]
    public void WhilePaused_TheSameInstant_LeavesThePauseUntouched()
    {
        var stored = Now.AddHours(3);

        var result = DispatchPause_Gate.Reconsider_WhilePaused(stored, anyReading: true, freshUntilUtc: stored);

        Assert.Equal(stored, result);
    }

    /// <summary>
    /// Silence is not a low number. No probe at all says nothing about the account, and lifting on it
    /// would turn "the status line stopped writing" into "the account is fine".
    /// </summary>
    [Fact]
    public void WhilePaused_NoReadingAtAll_LeavesThePauseStanding()
    {
        var stored = Now.AddDays(4);

        var result = DispatchPause_Gate.Reconsider_WhilePaused(stored, anyReading: false, freshUntilUtc: null);

        Assert.Equal(stored, result);
    }

    /// <summary>The binding window is the one that comes back LAST, whatever the enumeration order.</summary>
    [Fact]
    public void AcrossWindows_TheBindingPauseIsTheOneThatComesBackLast()
    {
        var windows = new Dictionary<string, (double Percent, DateTime? WindowResetsAtUtc)>
        {
            ["rate_limits.five_hour.used_percentage"] = (96, Now.AddMinutes(20)),
            ["rate_limits.seven_day.used_percentage"] = (99, Now.AddDays(3)),
            ["rate_limits.other.used_percentage"] = (10, Now.AddDays(6)),
        };

        var binding = DispatchPause_Gate.Decide_BindingPause_OrNull(windows, thresholdPercent: 95, nowUtc: Now);

        Assert.NotNull(binding);
        Assert.Equal(Now.AddDays(3), binding.Value.Until);
        Assert.Equal("rate_limits.seven_day.used_percentage", binding.Value.Window);
        Assert.Equal(99, binding.Value.Percent);
    }

    [Fact]
    public void AcrossWindows_NoneOverTheThreshold_DecidesNoPause()
    {
        var windows = new Dictionary<string, (double Percent, DateTime? WindowResetsAtUtc)>
        {
            ["rate_limits.five_hour.used_percentage"] = (91, null),
            ["rate_limits.seven_day.used_percentage"] = (12, Now.AddDays(5)),
        };

        Assert.Null(DispatchPause_Gate.Decide_BindingPause_OrNull(windows, thresholdPercent: 95, nowUtc: Now));
    }
}
