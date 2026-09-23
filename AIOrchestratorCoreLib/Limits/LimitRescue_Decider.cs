using AIOrchestratorCoreLib.Running;

namespace AIOrchestratorCoreLib.Limits;

/// <summary>
/// WHETHER A TERMINAL SESSION STILL STUCK ON A USAGE LIMIT IS RESTARTED, AND ON WHAT GROUNDS
/// (owner, 2026-09-23).
///
/// <para>
/// THE DEFECT. When a limit is hit, the interactive CLI either waits by itself
/// (<c>autoContinueAtUsageLimit</c>) or shows a menu and waits for a keypress. Either way it can stay
/// stuck: the menu has no default, the auto-wait turns into "press enter to continue" after a sleep
/// of more than 30 minutes, and a wait keeps counting down the OLD account's reset after the owner
/// logged in to another one. The app already knew such a session was blocked — it read the refusal
/// out of the transcript — but used that only to stop calling it deaf.
/// </para>
/// <para>
/// THE REMEDY IS A KILL, NOT A RESPAWN. The session's shell is stopped and the watchdog's ordinary
/// dead-session path brings it back — a supervisor or solo with <c>--resume</c> of its own
/// conversation (decision 8), an implementer fresh from its channel. One respawn path, so nothing
/// here can disagree with it about flags, resume ids or the crash-loop counter.
/// </para>
/// <para>
/// AT THE RESET, NOT BEFORE (Ruling L1, 2026-09-23). The first version of this asked only "dispatch
/// open ≥ 5 min, refusal ≥ 5 min old, no restart in 30 min" — it never asked whether the allowance was
/// back. That morning the five-hour window hit 100% at 09:13 UTC with "resets 12pm (Europe/Rome)",
/// 10:00 UTC, and dispatch never paused (a stale owner lift held it back), so it would have killed
/// every stuck session from 09:18 and every half hour after, into the same refusal. Now a restart
/// needs GROUNDS: the reset the refusal itself names has passed, plus <see cref="GRACE_MINUTES"/>
/// (<see cref="LimitRescueGrounds.ResetPassed"/>); or a fresh status-line probe shows the account
/// below its limits (<see cref="LimitRescueGrounds.ProbeShowsAllowance"/>); or the refusal came
/// before the owner logged in to another account, so its reset is not the allowance being spent now
/// (<see cref="LimitRescueGrounds.AccountSwitched"/>). A refusal that names no reset this app can
/// stand behind falls back to the first version's rule (<see cref="LimitRescueGrounds.NoResetKnown"/>).
/// </para>
/// <para>
/// GRACE ON BOTH CLOCKS, AND A CAP, whatever the grounds. The dispatcher must have been open for
/// <see cref="GRACE_MINUTES"/>, so the CLI's own auto-continue — which fires at the reset instant —
/// gets there first; the refusal itself must be that old, so a session refused a moment ago is not
/// killed into the same wall; and AT MOST ONCE PER <see cref="MIN_INTERVAL_MINUTES"/> per session: a
/// respawned session keeps showing the old refusal until its new process writes a reply.
/// </para>
/// </summary>
public static class LimitRescue_Decider
{
    public const int GRACE_MINUTES = 5;

    public const int MIN_INTERVAL_MINUTES = 30;

    /// <summary>How recent a probe file must be to speak for the account's allowance NOW.</summary>
    public const int FRESH_PROBE_MINUTES = 5;

    /// <summary>A reading at or above this is a spent window — the CLI refuses at 100%.</summary>
    const double SPENT_PERCENT = 100;

    /// <param name="refusedAtUtc">When the refusal the session is blocked on was said; null when it is not blocked (or we cannot tell).</param>
    /// <param name="refusalResetsAtUtc">The reset that refusal names (<see cref="Read_RefusalResetsAtUtc_OrNull"/>); null when it names none we trust.</param>
    /// <param name="accountSwitchedAtUtc">When the owner last logged in to a different account; null when never seen.</param>
    /// <param name="freshProbeShowsAllowance">A probe written since <see cref="Build_FreshProbeFloor"/> shows every live window below its limit.</param>
    public static LimitRescueGrounds? Decide_Grounds_OrNull(
        DateTime? refusedAtUtc,
        DateTime? refusalResetsAtUtc,
        DateTime? accountSwitchedAtUtc,
        bool freshProbeShowsAllowance,
        DateTime dispatchOpenSinceUtc,
        DateTime? lastRestartUtc,
        DateTime nowUtc)
    {
        if (refusedAtUtc == null)
            return null;

        if (lastRestartUtc != null && nowUtc - lastRestartUtc.Value < TimeSpan.FromMinutes(MIN_INTERVAL_MINUTES))
            return null;

        var grace = TimeSpan.FromMinutes(GRACE_MINUTES);

        if (nowUtc - dispatchOpenSinceUtc < grace || nowUtc - refusedAtUtc.Value < grace)
            return null;

        if (accountSwitchedAtUtc != null && refusedAtUtc.Value < accountSwitchedAtUtc.Value)
            return LimitRescueGrounds.AccountSwitched;

        if (refusalResetsAtUtc != null && nowUtc >= refusalResetsAtUtc.Value + grace)
            return LimitRescueGrounds.ResetPassed;

        if (freshProbeShowsAllowance)
            return LimitRescueGrounds.ProbeShowsAllowance;

        return refusalResetsAtUtc == null ? LimitRescueGrounds.NoResetKnown : null;
    }

    /// <summary>
    /// The reset a refusal names, read as of when it was said (<see cref="LimitReset_Parser.Read_ResetInstant_OrNull"/>),
    /// or null. A reading whose zone the machine could not resolve is DROPPED: the parser falls back to
    /// UTC with a caveat, which can be hours off in either direction, and a kill decided on it is the
    /// confident wrong number decision 12 is about — no reset at all is the honest reading.
    /// </summary>
    public static DateTime? Read_RefusalResetsAtUtc_OrNull(string? refusalText, int? apiStatus, DateTime refusedAtUtc)
    {
        var reading = LimitReset_Parser.Read_ResetInstant_OrNull(refusalText, apiStatus, refusedAtUtc);

        return reading == null || reading.ZoneAssumedUtc ? null : reading.ResetsAtUtc;
    }

    /// <summary>
    /// The oldest write a probe may carry and still speak for the allowance now: after the refusal
    /// (earlier readings are what led to it), after an account switch (the previous account's), and
    /// within <see cref="FRESH_PROBE_MINUTES"/>.
    /// </summary>
    public static DateTime Build_FreshProbeFloor(DateTime refusedAtUtc, DateTime? accountSwitchedAtUtc, DateTime nowUtc)
    {
        var floor = nowUtc.AddMinutes(-FRESH_PROBE_MINUTES);

        if (refusedAtUtc > floor)
            floor = refusedAtUtc;

        if (accountSwitchedAtUtc != null && accountSwitchedAtUtc.Value > floor)
            floor = accountSwitchedAtUtc.Value;

        return floor;
    }

    /// <summary>
    /// The account's live windows say the allowance is back: at least one reading, and none spent.
    /// No reading at all is "cannot tell", never "back" (decision 21).
    /// </summary>
    public static bool Shows_AllowanceBack(IReadOnlyCollection<double> liveWindowPercents)
    {
        return liveWindowPercents.Count > 0 && liveWindowPercents.All(percent => percent < SPENT_PERCENT);
    }

    public static string Describe_Restart(string slot, LimitRescueGrounds grounds, string refusalText)
    {
        var why = grounds switch
        {
            LimitRescueGrounds.ResetPassed => $"the reset its refusal named passed {GRACE_MINUTES}+ min ago",
            LimitRescueGrounds.ProbeShowsAllowance => "a fresh status-line probe shows the account below its limits",
            LimitRescueGrounds.AccountSwitched => "its refusal came before the owner logged in to another Claude account",
            _ => $"its refusal names no reset this app can read, and dispatch has been open {GRACE_MINUTES}+ min",
        };

        return $"{slot} was still stuck on a usage limit ('{refusalText}') and {why} — its terminal was stopped so the watchdog restarts it (a supervisor or solo resumes its own conversation)";
    }
}

/// <summary>Why a session stuck on a usage limit was judged free to restart — see <see cref="LimitRescue_Decider"/>.</summary>
public enum LimitRescueGrounds
{
    ResetPassed,
    ProbeShowsAllowance,
    AccountSwitched,
    NoResetKnown,
}
