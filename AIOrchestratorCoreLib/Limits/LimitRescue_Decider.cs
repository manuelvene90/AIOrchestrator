namespace AIOrchestratorCoreLib.Limits;

/// <summary>
/// WHETHER A TERMINAL SESSION STILL STUCK ON A USAGE LIMIT, AFTER THE ALLOWANCE CAME BACK, IS
/// RESTARTED (owner, 2026-09-23).
///
/// <para>
/// THE DEFECT. When a limit is hit, the interactive CLI either waits by itself
/// (<c>autoContinueAtUsageLimit</c>) or shows a menu and waits for a keypress. Either way it can stay
/// stuck: the menu has no default, the auto-wait turns into "press enter to continue" after a sleep
/// of more than 30 minutes, and a wait keeps counting down the OLD account's reset after the owner
/// logged in to another one. The app already knew such a session was blocked — it read the refusal
/// out of the transcript — but used that only to stop calling it deaf. When the window reset, the
/// pause lifted, a notice went out, and the session sat there until the owner came back to the PC.
/// </para>
/// <para>
/// THE REMEDY IS A KILL, NOT A RESPAWN. The session's shell is stopped and the watchdog's ordinary
/// dead-session path brings it back — a supervisor or solo with <c>--resume</c> of its own
/// conversation (decision 8), an implementer fresh from its channel. One respawn path, so nothing
/// here can disagree with it about flags, resume ids or the crash-loop counter.
/// </para>
/// <para>
/// GRACE ON BOTH CLOCKS. The dispatcher must have been open for <see cref="GRACE_MINUTES"/>, so the
/// CLI's own auto-continue — which fires at the reset instant — gets there first and turns the
/// session un-blocked without anyone touching it; and the refusal itself must be that old, so a
/// session refused a moment ago on an allowance nobody has measured yet is not killed into the same
/// wall. And AT MOST ONCE PER <see cref="MIN_INTERVAL_MINUTES"/> per session: a respawned session
/// keeps showing the old refusal until its new process writes its first status line, and one that
/// is refused again should cost one restart per half hour, not one per tick.
/// </para>
/// </summary>
public static class LimitRescue_Decider
{
    public const int GRACE_MINUTES = 5;

    public const int MIN_INTERVAL_MINUTES = 30;

    public static bool Should_Restart(
        bool blockedOnUsageLimit,
        DateTime? refusedAtUtc,
        DateTime dispatchOpenSinceUtc,
        DateTime? lastRestartUtc,
        DateTime nowUtc)
    {
        if (!blockedOnUsageLimit || refusedAtUtc == null)
            return false;

        var grace = TimeSpan.FromMinutes(GRACE_MINUTES);

        if (nowUtc - dispatchOpenSinceUtc < grace)
            return false;

        if (nowUtc - refusedAtUtc.Value < grace)
            return false;

        return lastRestartUtc == null || nowUtc - lastRestartUtc.Value >= TimeSpan.FromMinutes(MIN_INTERVAL_MINUTES);
    }

    public static string Describe_Restart(string slot)
    {
        return $"{slot} was still stuck on a usage limit {GRACE_MINUTES}+ min after the allowance came back — its terminal was stopped so the watchdog restarts it (a supervisor or solo resumes its own conversation)";
    }
}
