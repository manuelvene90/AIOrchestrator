namespace AIOrchestratorCoreLib.Channels.StatusLog;

/// <summary>
/// THE BOOKKEEPING THAT MAY LEAVE A CHANNEL — a closed list, one value per family in the 2026-09-15
/// one-wake-model plan 02's classification table. 78 places in this codebase write an app-authored
/// channel entry; 17 of them name a kind here and the other 61 do not, because they are the owner's
/// receipts or the conversation itself.
///
/// <para>
/// A NAMED KIND RATHER THAN A BOOLEAN AT THE CALL SITE. "route this one" has to be a decision with a
/// name that a reader can look up in the table, or the next person routes a nudge — and a nudge that
/// leaves the channel breaks the orphan escalation, which is the only proof a member's monitor is
/// dead (<c>Nudge_Decider</c>, and its test
/// <c>AnAppEntryStillCountsAsInbound_BecauseEscalationDependsOnIt</c>, left as a tripwire for exactly
/// this change).
/// </para>
/// <para>
/// THE KIND NEVER DECIDES WHERE. Every value here is movable by construction, and what actually
/// decides is the sink and the audience (<see cref="AppNote_Writer"/>). A kind that had to be
/// consulted for the destination would be a second policy beside
/// <see cref="BookkeepingSink_Policy"/>, which is the one thing this file exists to avoid.
/// </para>
/// </summary>
public enum AppNoteKinds
{
    /// <summary>The dispatcher's record of the turn that just ended. Nobody answers it; the session already knows.</summary>
    TurnEnded,

    /// <summary>
    /// Stall alerts, usage-limit notices, deadline-kill warnings, and the two coaching notices about a
    /// turn's own shape (a superseded final message, a reply addressed to a channel that is not a
    /// source). Owner-facing on a supervisor, solo or general session — and then it does not move.
    /// </summary>
    TurnMachinery,

    /// <summary>PLAN.md is behind the verdicts / has lines that cannot show progress / claims work nobody is doing.</summary>
    LedgerAdvisory,

    /// <summary>A member has been nudged and has not moved — <c>OrphanEscalation_Decider.Describe_Report</c>.</summary>
    OrphanReport,

    /// <summary>
    /// The message and question contracts, and the three question-deduplication notices. The session
    /// must read these — "your question was NOT sent to the owner" is the only trace that a question
    /// died — which is why the pack carries them (Task 11) rather than the log merely holding them.
    /// </summary>
    ContractCoaching,
}
