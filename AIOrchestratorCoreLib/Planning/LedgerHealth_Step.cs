using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Planning;

/// <summary>What the ledger check must do this tick: whether to alert, and what to remember.</summary>
public readonly record struct LedgerHealthOutcome(bool ShouldAppendAlert, bool RemembersReported);

/// <summary>
/// The ledger tick's two halves, in the one order that is safe — RECONCILE the block flag, THEN
/// decide the alert. It is a step of its own because the engine cannot be driven by a test (its only
/// entry point is <c>Run_Async</c>), and the order is the whole correctness of it.
///
/// <para>
/// THE WEDGE THIS PREVENTS, which two individually-correct fixes composed into. Terminal presence
/// must defer the app's attention traffic, so a meeting-time bail was added at the top of the ledger
/// check — above <see cref="LedgerHealth_Tracker.Sync_Flag"/>, which is the ONLY thing that deletes
/// <c>.ledger-behind</c>. That flag makes <c>supervisor-ledger-check.sh</c> refuse to end the
/// supervisor's turn. Meanwhile `/pc` began clearing <c>.awaiting-answer</c> — the very flag that
/// hook's deadlock guard exits on. So: the owner walks over, directs the supervisor to update the
/// ledger (directed work continues in a meeting), the supervisor writes PLAN.md, the debt is really
/// paid, and its turn end is still blocked by a hook telling it to write the file it just wrote,
/// with the escape hatch gone — for the length of the meeting, in the mode that exists to stop the
/// supervisor being wedged in front of the owner.
/// </para>
/// <para>
/// SUPPRESSION DEFERS THE ALERT, NEVER THE RECONCILIATION. An alert is attention; a flag is state,
/// and lifting a block is not an interruption. The tests assert the FLAG FILE rather than where any
/// guard sits, so an early exit re-added above this call for some future reason reddens them
/// wherever it is put.
/// </para>
/// </summary>
public static class LedgerHealth_Step
{
    public static LedgerHealthOutcome Reconcile(
        ISupervisionPaths paths,
        string orchId,
        bool isBehind,
        bool alreadyReported,
        bool suppressed)
    {
        // ALWAYS, meeting or not. See the wedge above.
        LedgerHealth_Tracker.Sync_Flag(paths, orchId, isBehind);

        if (!isBehind)
            return new LedgerHealthOutcome(ShouldAppendAlert: false, RemembersReported: false);

        // DEFERRED, NOT DROPPED. Returning the memo UNCHANGED is the whole fix: the once-per-spell
        // token is what makes the alert fire only once, so spending it while the entry goes nowhere
        // destroys the alert instead of holding it. It arrives on the first tick after the meeting.
        if (suppressed)
            return new LedgerHealthOutcome(ShouldAppendAlert: false, RemembersReported: alreadyReported);

        return new LedgerHealthOutcome(ShouldAppendAlert: !alreadyReported, RemembersReported: true);
    }
}
