using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.RegisteredSessions;

/// <summary>
/// EVERY SESSION THAT HAS A STATE FILE — the general supervisor, every open orchestration's
/// supervisor, and every open member of each — with no opinion at all about who runs its turns.
///
/// <para>
/// IT WAS <c>PrintTurnDispatcherModel.Discover_RegisteredSessions</c>, private, and the
/// one-wake-model sweep needs the same walk with a different screen
/// (<c>BridgeEngineModel.Sweep_WakeTickets_Async</c>: terminal runner, ticket wake). Copying it
/// would have been a second answer to "which sessions exist" — and the two would drift on the day a
/// role stops being in the member roster, exactly as the supervisor already is not (see below).
/// CLAUDE.md decision 12: never a second copy.
/// </para>
/// <para>
/// THE SCREEN IS THE CALLER'S. This returns registrations; the dispatcher then keeps the
/// bridge-driven ones and the sweep keeps the ticket-mode terminal ones.
/// </para>
/// </summary>
public static class RegisteredSessions_Reader
{
    public static IReadOnlyList<(string StateFile, SessionRoles Role, string OrchId, string MemberId)> Find_All(
        ISupervisionPaths paths,
        IOrchestrationSessionStore store)
    {
        List<(string, SessionRoles, string, string)> found = [];

        var generalFile = PrintSessionState_Store.Get_StateFile(paths, SessionRoles.General, ChannelDiscovery.GENERAL_ORCH_ID, SessionLaunch_Factory.GENERAL_MEMBER_ID);

        if (File.Exists(generalFile))
            found.Add((generalFile, SessionRoles.General, ChannelDiscovery.GENERAL_ORCH_ID, SessionLaunch_Factory.GENERAL_MEMBER_ID));

        foreach (var session in store.Load_All())
        {
            if (session.ClosedUtc != null)
                continue;

            // THE SUPERVISOR IS NOT IN THE MEMBER ROSTER — it is the orchestration itself — so a
            // loop over Members alone finds every implementer and never the one role the stream
            // runner exists for. Its registration lives under a role-prefixed name beside
            // session.json (PrintSessionState_Store knows where); found here or found nowhere.
            var supervisorFile = PrintSessionState_Store.Get_StateFile(paths, SessionRoles.Supervisor, session.OrchId, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID);

            if (File.Exists(supervisorFile))
                found.Add((supervisorFile, SessionRoles.Supervisor, session.OrchId, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID));

            foreach (var member in session.Members)
            {
                if (member.ClosedUtc != null)
                    continue;

                var role = SessionRole_Names.From_MemberKind(MemberKind_Ids.Resolve_Kind(member.MemberId));
                var stateFile = PrintSessionState_Store.Get_StateFile(paths, role, session.OrchId, member.MemberId);

                if (File.Exists(stateFile))
                    found.Add((stateFile, role, session.OrchId, member.MemberId));
            }
        }

        return found;
    }
}
