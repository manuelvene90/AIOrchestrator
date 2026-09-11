namespace AIOrchestratorCoreLib.Status;

/// <summary>How confidently the app can say a member is working.</summary>
public enum WorkingVerdicts
{
    /// <summary>A turn is running right now, or one ended moments ago. Positive knowledge.</summary>
    Working,

    /// <summary>
    /// A turn has been ADMITTED but is waiting for a concurrency slot — nothing is executing for this
    /// member. Positive knowledge, and NOT a spelling of Working: measured 2026-09-10 21:18→21:47 on
    /// the VPS, the owner was told "still at it" for 29 minutes about a supervisor that was sitting in
    /// the per-orchestration queue behind five implementer turns, and the session was told "the owner
    /// is waiting on you" while it could not even start.
    /// </summary>
    Queued,

    /// <summary>No turn running and nothing recent. Positive knowledge — it really is between turns.</summary>
    Idle,

    /// <summary>Nothing can be read. NOT idle, and never a reason to act.</summary>
    Unknown,
}

/// <summary>
/// WHETHER A MEMBER IS WORKING — answered from what the APP wrote, not from what a shell script was
/// supposed to write.
///
/// <para>
/// <b>THE FICTION THIS REPLACES.</b> Every "working now" / "idle — waiting" the app has ever shown,
/// the typing bubble, the stall alert, the periodic-status suppression and the orphan escalation all
/// resolved through <c>SessionActivity_Probe.Is_MidTurn</c>, whose only input is
/// <c>&lt;orch&gt;/&lt;member&gt;/.usage.json</c> — written by the Claude Code status line. A headless
/// <c>claude -p</c> session renders no status line, so on a bridge-driven host that file does not
/// exist, the probe returns Unknown, and <c>Is_MidTurn</c> collapses Unknown into a concrete
/// <c>false</c>. The owner was then told "idle — waiting" about members mid-turn; in the fincanva-1
/// run of 2026-09-07 the word "working now" never appeared once in two hours, across five members
/// that were demonstrably running turns.
/// </para>
/// <para>
/// <b>THE APP ALREADY KNEW.</b> It starts these turns itself. The dispatcher holds the in-flight map,
/// and every completed turn is recorded with its end time in <c>print-session.json</c>. Neither can
/// be moved by the app's own nudge — the defect that made channel mtime unusable for this — and
/// neither depends on anything outside the app.
/// </para>
/// <para>
/// <b>UNKNOWN IS A THIRD ANSWER, NOT A SPELLING OF IDLE.</b> That collapse is what let a missing file
/// become a destructive decision. Callers that display state should render Unknown as nothing at all
/// rather than as "idle"; callers that act must never act on it.
/// </para>
/// <para>
/// <b>THE GRACE WINDOW EXISTS FOR THE GAP BETWEEN TURNS.</b> A member that finished a turn a few
/// seconds ago and is about to be handed the next one is working, not idle, and the dispatcher's map
/// is empty for exactly that instant.
/// </para>
/// </summary>
public static class MemberWorking_Decider
{
    /// <summary>
    /// How long after a turn ends the member still counts as working. Wide enough to cover the
    /// dispatcher's gap between turns, far narrower than any nudge or orphan window, so it can never
    /// mask a member that has genuinely stopped.
    /// </summary>
    public const int RECENTLY_WORKED_SECONDS = 90;

    /// <param name="turnInFlight">The dispatcher has admitted a turn for this member (running OR waiting for a slot).</param>
    /// <param name="turnQueued">That admitted turn is still waiting for a concurrency slot — nothing executes yet.</param>
    /// <param name="lastTurnEndedUtc">End of the most recent completed turn, or null if none is recorded.</param>
    /// <param name="sessionRegistered">A bridge-driven session state exists — i.e. these inputs mean something.</param>
    /// <param name="nowUtc">Now.</param>
    public static WorkingVerdicts Decide(
        bool sessionRegistered,
        bool turnInFlight,
        bool turnQueued,
        DateTime? lastTurnEndedUtc,
        DateTime nowUtc)
    {
        // NO REGISTRATION, NO OPINION. A terminal-run member has no dispatcher and no turn record;
        // answering "idle" for it would rebuild the exact fiction this replaces, one layer up.
        if (!sessionRegistered)
            return WorkingVerdicts.Unknown;

        // QUEUED BEFORE WORKING: a queued turn is in the in-flight table too, and reading the table
        // alone is exactly how "still at it" was said about a session that had not started.
        if (turnInFlight && turnQueued)
            return WorkingVerdicts.Queued;

        if (turnInFlight)
            return WorkingVerdicts.Working;

        if (lastTurnEndedUtc == null)
            return WorkingVerdicts.Unknown;

        if ((nowUtc - lastTurnEndedUtc.Value).TotalSeconds <= RECENTLY_WORKED_SECONDS)
            return WorkingVerdicts.Working;

        // REGISTERED, NOTHING RUNNING, NOTHING RECENT. This is the one case the app can assert, and
        // it is the only one that may be shown to the owner as idle.
        return WorkingVerdicts.Idle;
    }

    /// <summary>
    /// For the guards. Working and Unknown both mean "do not act" — the asymmetry is deliberate and
    /// is the whole lesson of the 19 false ORPHANED events: acting needs knowledge, sparing does not.
    /// </summary>
    public static bool Safe_ToDisturb(WorkingVerdicts verdict)
    {
        return verdict == WorkingVerdicts.Idle;
    }

    /// <summary>
    /// For the guards that ask "is this member's turn occupied": Working and Queued both are — a
    /// queued member can no more read its channel than a running one, so waking it is a wasted
    /// turn either way. Unknown is NOT busy; it is the absence of an answer.
    /// </summary>
    public static bool Is_Busy(WorkingVerdicts verdict)
    {
        return verdict is WorkingVerdicts.Working or WorkingVerdicts.Queued;
    }

    /// <summary>
    /// For the surfaces. Null means SAY NOTHING — never "idle", which would be an assertion the app
    /// cannot back.
    /// </summary>
    public static string? Describe_OrNull(WorkingVerdicts verdict)
    {
        return verdict switch
        {
            WorkingVerdicts.Working => "working now",
            WorkingVerdicts.Queued => QUEUED_WORDS,
            WorkingVerdicts.Idle => "between turns",
            _ => null,
        };
    }

    /// <summary>The one wording for a turn that is admitted but has no slot yet — shared by every surface.</summary>
    public const string QUEUED_WORDS = "queued — waiting for a free turn slot";
}
