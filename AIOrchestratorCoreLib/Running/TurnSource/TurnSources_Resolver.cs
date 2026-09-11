using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.TurnSource;

/// <summary>
/// EVERY CHANNEL A ROLE IS WOKEN BY. One table, resolved fresh whenever it is asked for, so a member
/// added mid-life becomes a source of its supervisor's next turn without anything being re-registered.
///
/// <para>
/// This replaces <c>PrintSessionState_Store.Resolve_ChannelFile</c>'s single answer for the supervisor
/// and keeps it for everyone else — a member, a solo and the general supervisor genuinely do have one
/// channel each, and pretending otherwise would put an empty <c>TO:</c> protocol in front of five roles
/// that never need it.
/// </para>
/// <para>
/// CLOSED MEMBERS ARE NOT SOURCES. A closed spoke can still be written to by a human reading the file,
/// but the session it belonged to is gone and its traffic is history, not a question. This is the same
/// screen <c>PrintTurnDispatcher</c>'s discovery applies to registrations.
/// </para>
/// <para>
/// DEDUPED BY PATH, not by key: a BASIC orchestration's solo writes into the owner channel rather than
/// a spoke of its own (<see cref="MemberChannel_Locator"/>), so a naive roster walk would list the same
/// file twice — once as <c>owner</c> and once as <c>solo-1</c> — and the same entry would then be
/// delivered twice under two cursors.
/// </para>
/// </summary>
public static class TurnSources_Resolver
{
    public static IReadOnlyList<ITurnSource> Resolve(ISupervisionPaths paths, IOrchestrationSessionStore store, SessionRoles role, string orchId, string memberId)
    {
        if (role != SessionRoles.Supervisor)
            return [Resolve_Own(paths, role, orchId, memberId)];

        List<ITurnSource> sources = [Resolve_Own(paths, role, orchId, memberId)];
        // CASE-INSENSITIVE, because the thing being deduped is a FILE. The dedupe exists so a basic
        // orchestration's solo — which writes into the owner channel rather than a spoke — is not listed
        // twice and delivered twice; today it works only because both call sites happen to build the
        // string identically. On a case-insensitive filesystem two spellings of one path would otherwise
        // become two sources with two cursors over one file, which is every entry delivered twice.
        HashSet<string> seenPaths = new([sources[0].ChannelFilePath], StringComparer.OrdinalIgnoreCase);

        var session = store.Get_Session_OrNull(orchId);

        if (session == null)
            return sources;

        foreach (var member in session.Members)
        {
            if (member.ClosedUtc != null)
                continue;

            var channelFile = MemberChannel_Locator.Get_ChannelFile(paths, orchId, member.MemberId);

            if (!seenPaths.Add(channelFile))
                continue;

            sources.Add(TurnSource_Factory.Create_Spoke(member.MemberId, channelFile));
        }

        return sources;
    }

    /// <summary>
    /// The session's OWN channel — where its <c>turn_ended</c> record, its stall alert and any reply it
    /// addressed to nobody are written. For every role but the supervisor it is also its only source.
    /// </summary>
    public static ITurnSource Resolve_Own(ISupervisionPaths paths, SessionRoles role, string orchId, string memberId)
    {
        return role switch
        {
            SessionRoles.Implementer or SessionRoles.Reviewer => TurnSource_Factory.Create_Spoke(memberId, paths.Get_ImplementerChannelFile(orchId, memberId)),
            SessionRoles.Solo or SessionRoles.Supervisor => TurnSource_Factory.Create_Owner(paths.Get_OwnerChannelFile(orchId)),
            SessionRoles.General => TurnSource_Factory.Create_Owner(paths.GeneralChannelFile),

            // THE COMMUNICATOR HAS NO CHANNEL TO BE WOKEN BY, and answering "the owner's" was a wrong
            // answer sitting in the one place this rule lives. Its input is the SUPERVISOR'S TRANSCRIPT;
            // it narrates and never works, so pointing it at the owner channel would have it answering
            // the owner directly the moment somebody enabled it. Unreachable today —
            // <see cref="Runner_Support.Supports"/> is false for it on every bridge-driven runner — so
            // this refuses loudly rather than handing the next person a plausible default they would not
            // think to question.
            SessionRoles.Communicator => throw new Exception($"The communicator has no channel source: it reads the supervisor's transcript, not a channel ('{orchId}/{memberId}'). Whatever wakes it has to be decided before it can be bridge-driven."),
            _ => throw new Exception($"Unhandled SessionRoles: {role}"),
        };
    }
}
