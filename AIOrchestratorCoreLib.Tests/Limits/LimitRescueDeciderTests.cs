using AIOrchestratorCoreLib.Limits;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Limits;

/// <summary>
/// When a terminal session still stuck on a usage limit is restarted — see
/// <see cref="LimitRescue_Decider"/> for the two incidents this answers.
/// </summary>
public class LimitRescueDeciderTests
{
    const string ROME_NOON = "You've hit your session limit · resets 12pm (Europe/Rome)";

    static readonly DateTime Now = new(2026, 9, 23, 3, 0, 0, DateTimeKind.Utc);
    static readonly DateTime LongOpen = Now.AddHours(-1);
    static readonly DateTime LongRefused = Now.AddHours(-3);

    static DateTime Utc(int hour, int minute) => new(2026, 9, 23, hour, minute, 0, DateTimeKind.Utc);

    /// <summary>The rule for a refusal that names no reset (today's rule, kept as the fallback).</summary>
    static LimitRescueGrounds? Decide_NoReset(DateTime? refusedAtUtc, DateTime dispatchOpenSinceUtc, DateTime? lastRestartUtc, DateTime nowUtc)
    {
        return LimitRescue_Decider.Decide_Grounds_OrNull(refusedAtUtc, null, null, () => false, dispatchOpenSinceUtc, lastRestartUtc, nowUtc);
    }

    /// <summary>
    /// THE MORNING OF 2026-09-23, on a fixed clock. The five-hour window hit 100% at 09:13 UTC — 11:13
    /// in Rome — and the refusal read "resets 12pm (Europe/Rome)", 10:00 UTC. Dispatch never paused (a
    /// stale owner lift held it back), so under the rule this branch first shipped every stuck session
    /// was killed from 09:18 and every half hour after, respawned into the same refusal. Now: nothing
    /// at 11:30 or 11:50 Rome, one restart at 12:05, none again before 12:35.
    /// </summary>
    [Fact]
    public void ARefusalNamingItsReset_IsRestartedAfterTheResetAndTheGrace_NotBefore()
    {
        var refusedAt = Utc(9, 13);
        var dispatchOpenSince = Utc(6, 0);
        var resetsAt = LimitRescue_Decider.Read_RefusalResetsAtUtc_OrNull(ROME_NOON, 429, refusedAt);

        Assert.Equal(Utc(10, 0), resetsAt);

        LimitRescueGrounds? At(DateTime nowUtc, DateTime? lastRestartUtc = null)
            => LimitRescue_Decider.Decide_Grounds_OrNull(refusedAt, resetsAt, null, () => false, dispatchOpenSince, lastRestartUtc, nowUtc);

        Assert.Null(At(Utc(9, 30)));
        Assert.Null(At(Utc(9, 50)));
        Assert.Null(At(Utc(10, 4)));
        Assert.Equal(LimitRescueGrounds.ResetPassed, At(Utc(10, 5)));

        // Restarted at 12:05 Rome, and the new process has not written a reply yet.
        Assert.Null(At(Utc(10, 20), lastRestartUtc: Utc(10, 5)));
        Assert.Null(At(Utc(10, 34), lastRestartUtc: Utc(10, 5)));
        Assert.Equal(LimitRescueGrounds.ResetPassed, At(Utc(10, 35), lastRestartUtc: Utc(10, 5)));
    }

    /// <summary>
    /// A FRESH PROBE SAYING THE ALLOWANCE IS BACK is the other way in: it can come before the named
    /// reset (a reset the CLI rounded), but never inside the grace of the refusal itself.
    /// </summary>
    [Fact]
    public void AFreshProbeShowingTheAllowanceBack_RestartsBeforeTheNamedReset()
    {
        var refusedAt = Utc(9, 13);

        Assert.Equal(
            LimitRescueGrounds.ProbeShowsAllowance,
            LimitRescue_Decider.Decide_Grounds_OrNull(refusedAt, Utc(10, 0), null, () => true, Utc(6, 0), null, Utc(9, 58)));

        Assert.Null(LimitRescue_Decider.Decide_Grounds_OrNull(refusedAt, Utc(10, 0), null, () => true, Utc(6, 0), null, Utc(9, 16)));
    }

    /// <summary>
    /// THE PROBE IS READ LAST, AND ONLY WHEN IT CAN DECIDE: it globs every usage file on the machine, so
    /// a session inside a grace or the cap, or one whose named reset has already passed, never pays for
    /// it (review of 171a23d — the probe used to be evaluated before the decider was even called).
    /// </summary>
    [Fact]
    public void TheProbe_IsNotReadWhenAnEarlierGateDecides()
    {
        var refusedAt = Utc(9, 13);
        var probeReads = 0;

        bool Probe()
        {
            probeReads++;
            return true;
        }

        LimitRescue_Decider.Decide_Grounds_OrNull(refusedAt, Utc(10, 0), null, Probe, Utc(6, 0), null, Utc(9, 16));
        LimitRescue_Decider.Decide_Grounds_OrNull(refusedAt, Utc(10, 0), null, Probe, Utc(6, 0), Utc(10, 0), Utc(10, 10));
        LimitRescue_Decider.Decide_Grounds_OrNull(refusedAt, Utc(10, 0), null, Probe, Utc(6, 0), null, Utc(10, 6));
        Assert.Equal(0, probeReads);

        LimitRescue_Decider.Decide_Grounds_OrNull(refusedAt, Utc(10, 0), null, Probe, Utc(6, 0), null, Utc(9, 58));
        Assert.Equal(1, probeReads);
    }

    /// <summary>
    /// A REFUSAL FROM BEFORE AN ACCOUNT SWITCH is the previous account's: the reset it names is not the
    /// allowance being spent now (the account-switch half of b2b7d8a, owner 2026-09-23).
    /// </summary>
    [Fact]
    public void ARefusalFromBeforeAnAccountSwitch_IsRestartedWithoutWaitingForItsReset()
    {
        var refusedAt = Utc(5, 1);
        var weeklyReset = new DateTime(2026, 9, 29, 11, 0, 0, DateTimeKind.Utc);

        Assert.Equal(
            LimitRescueGrounds.AccountSwitched,
            LimitRescue_Decider.Decide_Grounds_OrNull(refusedAt, weeklyReset, Utc(5, 3), () => false, Utc(5, 3), null, Utc(5, 9)));

        // A refusal AFTER the switch is the new account's own, and waits for its reset.
        Assert.Null(LimitRescue_Decider.Decide_Grounds_OrNull(Utc(5, 4), weeklyReset, Utc(5, 3), () => false, Utc(5, 3), null, Utc(5, 30)));
    }

    /// <summary>
    /// A refusal naming no reset falls back to the old rule — dispatch open past the grace, the refusal
    /// past the grace — and the half-hour cap still bounds it.
    /// </summary>
    [Fact]
    public void ARefusalNamingNoReset_FallsBackToTheOldRule()
    {
        Assert.Equal(LimitRescueGrounds.NoResetKnown, Decide_NoReset(LongRefused, LongOpen, null, Now));
        Assert.Equal(
            LimitRescueGrounds.ProbeShowsAllowance,
            LimitRescue_Decider.Decide_Grounds_OrNull(LongRefused, null, null, () => true, LongOpen, null, Now));
    }

    [Fact]
    public void ASessionThatIsNotBlocked_IsNeverRestarted()
    {
        Assert.Null(Decide_NoReset(null, LongOpen, null, Now));
    }

    /// <summary>
    /// Inside the grace after the window reopened, the CLI's own auto-continue gets its chance first —
    /// it fires at the reset instant, and killing it then would throw away a turn already restarting.
    /// </summary>
    [Fact]
    public void JustAfterTheDispatcherReopened_ItWaitsForTheGrace()
    {
        var justOpened = Now.AddMinutes(-(LimitRescue_Decider.GRACE_MINUTES - 1));

        Assert.Null(Decide_NoReset(LongRefused, justOpened, null, Now));
        Assert.NotNull(Decide_NoReset(LongRefused, Now.AddMinutes(-LimitRescue_Decider.GRACE_MINUTES), null, Now));
    }

    /// <summary>A refusal from a moment ago is an allowance nobody has measured yet — not a stuck session.</summary>
    [Fact]
    public void AFreshRefusal_IsNotRestarted()
    {
        var justRefused = Now.AddMinutes(-(LimitRescue_Decider.GRACE_MINUTES - 1));

        Assert.Null(Decide_NoReset(justRefused, LongOpen, null, Now));
    }

    /// <summary>
    /// One restart per half hour per session: the respawned session shows the OLD refusal until its
    /// new process writes a reply, and a session refused again must not be killed every tick.
    /// </summary>
    [Fact]
    public void ARecentlyRestartedSession_IsNotRestartedAgainInsideTheInterval()
    {
        var restarted = Now.AddMinutes(-(LimitRescue_Decider.MIN_INTERVAL_MINUTES - 1));
        var restartedLongAgo = Now.AddMinutes(-LimitRescue_Decider.MIN_INTERVAL_MINUTES);

        Assert.Null(Decide_NoReset(LongRefused, LongOpen, restarted, Now));
        Assert.NotNull(Decide_NoReset(LongRefused, LongOpen, restartedLongAgo, Now));
    }

    /// <summary>
    /// A ZONE THE MACHINE COULD NOT RESOLVE is read in UTC by the parser, WITH a caveat — hours off in
    /// either direction. A kill decided on it is the confident wrong number of decision 12, so the
    /// rescue treats it as no reset at all.
    /// </summary>
    [Fact]
    public void AResetReadInAnAssumedZone_CountsAsNoReset()
    {
        Assert.Null(LimitRescue_Decider.Read_RefusalResetsAtUtc_OrNull("You've hit your session limit · resets 12pm (Mars/Olympus)", 429, Utc(9, 13)));
        Assert.Null(LimitRescue_Decider.Read_RefusalResetsAtUtc_OrNull("You've reached your Fable limit. Run /usage-credits to continue.", 429, Utc(9, 13)));
        Assert.Null(LimitRescue_Decider.Read_RefusalResetsAtUtc_OrNull(null, 429, Utc(9, 13)));
    }

    /// <summary>
    /// THE PROBE READING: the account's live windows, from probes written since the floor. At least one
    /// reading, and every one below 100% — an empty list is "cannot tell", never "back".
    /// </summary>
    [Fact]
    public void TheProbeShowsTheAllowanceBack_OnlyWhenEveryLiveWindowIsBelowTheLimit()
    {
        Assert.True(LimitRescue_Decider.Shows_AllowanceBack([3, 41]));
        Assert.False(LimitRescue_Decider.Shows_AllowanceBack([100, 41]));
        Assert.False(LimitRescue_Decider.Shows_AllowanceBack([]));
    }

    /// <summary>A probe counts only if written after the refusal, after an account switch, and within the freshness window.</summary>
    [Fact]
    public void TheFreshProbeFloor_IsTheLatestOfTheRefusalTheSwitchAndTheFreshnessWindow()
    {
        var now = Utc(10, 0);

        Assert.Equal(now.AddMinutes(-LimitRescue_Decider.FRESH_PROBE_MINUTES), LimitRescue_Decider.Build_FreshProbeFloor(Utc(9, 13), null, now));
        Assert.Equal(Utc(9, 58), LimitRescue_Decider.Build_FreshProbeFloor(Utc(9, 58), null, now));
        Assert.Equal(Utc(9, 59), LimitRescue_Decider.Build_FreshProbeFloor(Utc(9, 13), Utc(9, 59), now));
    }
}
