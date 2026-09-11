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

        var text = DispatchPause_Gate.Describe_Pause("5-hour", 97.3, resumeAt);

        Assert.Contains("5-hour", text);
        Assert.Contains("97.3", text);
        Assert.Contains(resumeAt.ToString("HH:mm"), text);
    }

    [Fact]
    public void Describe_Resume_CarriesTheReasonItIsGiven()
    {
        var text = DispatchPause_Gate.Describe_Resume("the 5-hour window reset");

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains("the 5-hour window reset", text);
    }
}
