namespace AIOrchestratorCoreLib.Bridge;

/// <summary>What to do about a member that has not visibly answered its nudge.</summary>
public enum OrphanEscalations
{
    /// <summary>The confirmation window has not closed yet.</summary>
    NotDue,

    /// <summary>
    /// The session is bridge-driven. It has no monitor to be deaf with, and the watchdog owns its
    /// liveness. This path has nothing to say about it.
    /// </summary>
    LeaveAlone_BridgeDriven,

    /// <summary>Positive evidence of life: a turn in flight, or activity after the nudge.</summary>
    LeaveAlone_Working,

    /// <summary>No evidence either way. Not-knowing is never death.</summary>
    LeaveAlone_Unknown,

    /// <summary>Positive evidence that the wake went unanswered. Report it; do not act on it.</summary>
    ReportDeaf,
}

/// <summary>
/// WHETHER A MEMBER THAT WENT QUIET AFTER A NUDGE IS ACTUALLY DEAF — and on the production host the
/// honest answer is almost always "no idea", which is why this exists.
///
/// <para>
/// <b>THE INCIDENT.</b> On 2026-09-07 one orchestration produced 19 ORPHANED events in three hours
/// across five members, and every single one was a false positive. Measured from the run's own logs:
/// seven members were demonstrably mid-work inside their own six-minute confirmation window, three of
/// those completed the turn successfully minutes later — one of them 18 seconds after being declared
/// idle, another filed a coherent STANDING BY report 11 seconds after — and three more were correctly
/// standing by with a named dependency. None was deaf. None was dead.
/// </para>
/// <para>
/// <b>WHY THE OLD TEST COULD NOT TELL.</b> Its only input was
/// <c>&lt;orch&gt;/&lt;member&gt;/.usage.json</c>, written by the Claude Code status line — and a
/// headless <c>claude -p</c> session renders no status line, so on any bridge-driven host that file
/// cannot exist. <c>SessionActivity_Probe</c> therefore returned Unknown on every call, and the
/// escalation's <c>lastActivityUtc != null &amp;&amp; &gt; nudgedUtc</c> test sent every Unknown down
/// the same branch as a corpse. The subsystem's own contract forbids exactly that
/// (<c>SessionActivity_Probe</c>: "Null when we cannot tell, which callers must read as 'no
/// opinion', never as death"), and the predicate written to enforce it had no callers.
/// </para>
/// <para>
/// <b>THE REAL DISCRIMINATOR WAS TURN LENGTH, NOT HEALTH.</b> A sister orchestration ran five
/// members, 58 turns and two hours with ZERO nudges — busier per minute, and untouched. The only
/// difference: no turn over five minutes, against eleven here and a 21-minute maximum. A member
/// inside a long turn cannot consume its channel, so its unread entries age past the nudge threshold
/// and it is condemned for being busy. Unread-entry age measures how OCCUPIED or how BLOCKED a
/// member is; it has never measured whether one is alive.
/// </para>
/// <para>
/// <b>AND THE ESCALATION IS A REPORT NOW, NOT A KILL.</b> Recover_OrphanedImplementer_Async was
/// introduced once and narrowed five times, every narrowing reacting to a false positive, and the
/// repo records no case of it ever rescuing a genuinely stuck member. The founding incident was
/// resolved by a human typing into a terminal, before the code existed, and its own write-up says
/// "the watchdog never kills — it only spawns". So a positive determination now asks the supervisor
/// to look, and leaves the process alone.
/// </para>
/// <para>
/// <b>THE ORDER OF THE CHECKS IS THE DESIGN.</b> Bridge-driven is asked before anything is read from
/// disk, so a host that can never produce the evidence never reaches the branch that weighs it.
/// </para>
/// </summary>
public static class OrphanEscalation_Decider
{
    /// <param name="isBridgeDriven">The session is print- or stream-run: no monitor, no pid file.</param>
    /// <param name="sinceNudge">How long since the nudge was written.</param>
    /// <param name="confirmWindow">How long a nudged member may stay silent before it is weighed.</param>
    /// <param name="hasOpenToolCall">A tool call is in flight — a long build, a big read.</param>
    /// <param name="lastActivityUtc">Newest transcript activity, or null for "cannot tell".</param>
    /// <param name="nudgedUtc">When the nudge was written.</param>
    public static OrphanEscalations Decide(
        bool isBridgeDriven,
        TimeSpan sinceNudge,
        TimeSpan confirmWindow,
        bool hasOpenToolCall,
        DateTime? lastActivityUtc,
        DateTime nudgedUtc)
    {
        if (sinceNudge < confirmWindow)
            return OrphanEscalations.NotDue;

        // FIRST, AND BEFORE ANY EVIDENCE IS WEIGHED. A bridge-driven member has no in-session monitor
        // for this path to find dead, and its process liveness belongs to the watchdog, which already
        // asks this same question and skips the same slots. Asking it here too is what stops the 19.
        if (isBridgeDriven)
            return OrphanEscalations.LeaveAlone_BridgeDriven;

        // A TURN IN FLIGHT IS LIFE, and this guard existed already but only upstream, where an
        // unreadable file made it silently false. Asked here it protects the member at the moment
        // the decision is actually taken.
        if (hasOpenToolCall)
            return OrphanEscalations.LeaveAlone_Working;

        if (lastActivityUtc == null)
            return OrphanEscalations.LeaveAlone_Unknown;

        if (lastActivityUtc > nudgedUtc)
            return OrphanEscalations.LeaveAlone_Working;

        return OrphanEscalations.ReportDeaf;
    }

    /// <summary>
    /// Whether the supervisor should be asked to look. Nothing in this file authorises touching a
    /// process, and no caller should reintroduce one.
    /// </summary>
    public static bool Reports(OrphanEscalations escalation)
    {
        return escalation == OrphanEscalations.ReportDeaf;
    }

    /// <summary>
    /// Whether the orphan clock should be cleared. Every decided outcome clears it — the member has
    /// been weighed once for this nudge, and re-weighing it every tick is how the old loop re-fed
    /// itself.
    /// </summary>
    public static bool Clears_TheClock(OrphanEscalations escalation)
    {
        return escalation != OrphanEscalations.NotDue;
    }

    /// <summary>
    /// What the log should say. Every outcome is written down: after this change the common case is
    /// silence toward the owner, and silence with no record would be indistinguishable from the
    /// detector having been switched off.
    /// </summary>
    public static string Describe(OrphanEscalations escalation, string memberId)
    {
        return escalation switch
        {
            OrphanEscalations.LeaveAlone_BridgeDriven =>
                $"{memberId} is bridge-driven — it has no monitor to be deaf with, so the orphan check does not apply",
            OrphanEscalations.LeaveAlone_Working =>
                $"{memberId} is working — it answered the nudge, or a tool call is in flight",
            OrphanEscalations.LeaveAlone_Unknown =>
                $"{memberId} cannot be read, so nothing is concluded — not knowing is not death",
            OrphanEscalations.ReportDeaf =>
                $"{memberId} took no turn after its nudge and has no tool call in flight — the supervisor is being asked to look",
            _ => $"{memberId} is not due to be weighed yet",
        };
    }
}
