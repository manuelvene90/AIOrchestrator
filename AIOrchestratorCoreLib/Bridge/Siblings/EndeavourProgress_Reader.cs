using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Planning.PlanProgress;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Storage;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Bridge.Siblings;

/// <summary>
/// THE ENDEAVOUR BAR IS COMPUTED, NEVER WRITTEN (spec 2026-09-23 §3.5): the sum of every member's own
/// PLAN.md, each read through <see cref="PlanLedger_Parser.Parse_OrNull"/> — so the <c>PARKED</c> and
/// <c>OWNER REQUESTS</c> sections are skipped here exactly as they are in each member's own bar, and the
/// sum can never count a line the member's topic does not. A shared ledger file was rejected: two sessions
/// Edit-ing one PLAN.md is the <c>imp-3</c> wipe moved to the ledger, and the Stop hooks read
/// <c>$AIORCH_ID/PLAN.md</c>.
/// <para>
/// CLOSED MEMBERS ARE SUMMED. The caller passes every member, open and closed
/// (<c>EndeavourMembers_Resolver.Resolve_All</c>): a finished job that closed and dropped out would make the
/// bar go BACKWARDS at the moment it succeeded — decision 13's non-monotonic-count trap in another shape.
/// </para>
/// <para>
/// There is no <c>Open</c> to sum, although §3.5 lists one: <see cref="IPlanProgress"/> carries no such
/// count — what is open is <c>Total − Done − InProgress − Blocked</c>, and it follows from the summed parts.
/// </para>
/// </summary>
public static class EndeavourProgress_Reader
{
    /// <summary>
    /// The summed ledger of <paramref name="members"/>, in member order; null when no member has a parseable
    /// PLAN.md — "no ledger yet" is a different answer from "0/0 done", and a bar of zeros would say the latter.
    /// Members with no ledger add nothing, exactly as they add nothing to their own topic's bar.
    /// </summary>
    public static IPlanProgress? Sum_OrNull(ISupervisionPaths paths, IReadOnlyList<IOrchestrationSession> members)
    {
        List<IPlanProgress> ledgers = [];

        foreach (var member in members)
        {
            // THE SAME READ the member's own counts line makes (ProgressReport_Builder.Build_CountsLine): a
            // ledger the topic shows is never missing from the sum, and an unreadable one is missing from both.
            var progress = PlanLedger_Parser.Parse_OrNull(Safe_FileReader.Read_AllText_OrEmpty(paths.Get_PlanFile(member.OrchId)));

            if (progress != null)
                ledgers.Add(progress);
        }

        if (ledgers.Count == 0)
            return null;

        List<string> inProgressTasks = [.. ledgers.SelectMany(ledger => ledger.InProgressTasks)];
        List<string> openTasks = [.. ledgers.SelectMany(ledger => ledger.OpenTasks)];

        return PlanProgress_Factory.Create(
            done: ledgers.Sum(ledger => ledger.Done),
            inProgress: ledgers.Sum(ledger => ledger.InProgress),
            blocked: ledgers.Sum(ledger => ledger.Blocked),
            notDoing: ledgers.Sum(ledger => ledger.NotDoing),
            total: ledgers.Sum(ledger => ledger.Total),
            currentTaskText: inProgressTasks.FirstOrDefault() ?? openTasks.FirstOrDefault(),
            inProgressTasks: inProgressTasks,
            blockedTasks: [.. ledgers.SelectMany(ledger => ledger.BlockedTasks)],
            openTasks: openTasks,
            doneTasks: [.. ledgers.SelectMany(ledger => ledger.DoneTasks)],
            lines: [.. ledgers.SelectMany(ledger => ledger.Lines)],
            blockedOnOwner: ledgers.Sum(ledger => ledger.BlockedOnOwner));
    }
}
