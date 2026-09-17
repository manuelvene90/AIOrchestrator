using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Reviewing;

/// <summary>
/// THE PENDING ENTRIES THAT RIDE THE SUPERVISOR'S NEXT TURN INSTEAD OF STARTING ONE — exactly the
/// fix reports the app has already relayed to a reviewer.
///
/// <para>
/// WHY THIS IS NOT A NEW IDEA. <c>PrintTurn_Trigger.Select_AgentNotes</c> already carries entries a
/// turn should READ but that must not BUY one; this is that rule applied to one member entry, for the
/// same reason — the supervisor will read the fix report on the turn where a verdict is possible,
/// which is the turn the re-review findings start. A turn spent in between can only say "noted", and
/// a supervisor wake-up is ~1 M input tokens (measured, VPS 6–9 Sep 2026).
/// </para>
/// <para>
/// NOTHING IS CONSUMED HERE. The identity is withheld from the WAKE-UP RULES and from nothing else:
/// the entry stays pending, its cursor does not move, and whatever starts the next turn hands it over
/// with everything else — "a turn takes every pending entry there is" is what makes holding safe, and
/// it is <c>PendingTraffic.WakeUp_Policy</c>'s own argument for the digest.
/// </para>
/// <para>
/// THE SUPERVISOR ONLY. A member's brief is the WORK, and holding work is not a saving — the same
/// objection <c>Bridge.QuestionHold_Policy</c> raises against holding member channels. And only a
/// ROUTED contract holds: one that is merely declared means the reviewer has not been told anything,
/// so the supervisor is still the only session that knows the round is open.
/// </para>
/// <para>
/// WHAT IT DOES NOT BUY, ANYWHERE. This governs the wake DECISION, which is the whole of a session's
/// wake-up under <c>WakeModes.Ticket</c> and none of it under <c>watcher</c> — there a bash monitor
/// watches the channel files directly and still wakes the supervisor on the same append. The saving
/// is claimable per session, once that session is on tickets, and not before.
/// </para>
/// </summary>
public static class RoutedHold_Policy
{
    public static IReadOnlyCollection<string> Resolve_RidingOnly(ISupervisionPaths paths, IPrintSessionState state)
    {
        if (state.Role != SessionRoles.Supervisor)
            return [];

        HashSet<string> riding = [];

        foreach (var contract in RerouteContract_Store.Read_Open(paths, state.OrchId))
        {
            if (contract.State == RerouteStates.Routed && contract.ReportIdentity != null)
                riding.Add(contract.ReportIdentity);
        }

        return riding;
    }
}
