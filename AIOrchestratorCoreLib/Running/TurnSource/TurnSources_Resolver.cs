using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Sessions;
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
/// <para>
/// A LINKED SOLO IS ALSO WOKEN BY ITS SIBLINGS' OUTBOXES (sibling plan 2026-09-23, spec §5.4): one
/// <see cref="TurnSourceKinds.Sibling"/> source per OPEN sibling, from <see cref="EndeavourMembers_Resolver"/>
/// (membership is derived, never a stored list). Two gates, both on the READER's orchestration:
/// <list type="bullet">
/// <item>a CLOSED or UNLINKED solo has only its own channel;</item>
/// <item>a PAUSED solo resolves NO sibling source. That is the pause gate for this waker (the PAUSE bullet of
/// CLAUDE.md: "miss one and dormancy is a word"), and the terminal watcher's "sibling traffic waits for them"
/// (§5.3). The dispatcher never drops a cursor for a source that merely did not resolve, so on unpause the
/// source comes back with its cursor and the backlog rides one turn — WITHIN THE LIVE FILE. The dispatcher
/// reads only the live outbox, and compaction (above 90 entries, keeping 45) archives what the reader has
/// not been handed: a sibling that writes that much during a long pause loses its oldest entries to
/// <c>sibling-outbox.archive.md</c>, and all the dispatcher does is log a warning
/// (<c>Warn_IfEntriesWereArchivedUndelivered</c>). The reader's recourse is the archive, which the solo's
/// role prose teaches it to read (Task 16).</item>
/// </list>
/// A PAUSED SIBLING is still a source for a live one — pause is about waking the paused session, and a
/// paused session writes nothing to wake anybody with.
/// </para>
/// </summary>
public static class TurnSources_Resolver
{
    public static IReadOnlyList<ITurnSource> Resolve(ISupervisionPaths paths, IOrchestrationSessionStore store, SessionRoles role, string orchId, string memberId)
    {
        if (role == SessionRoles.Solo)
            return Resolve_WithSiblings(paths, store, Resolve_Own(paths, role, orchId, memberId), orchId);

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
    /// THE SOURCES A REPLY MAY BE WRITTEN INTO — every one but a sibling's outbox. The one exception to
    /// <see cref="ITurnSource"/>'s "source and reply target are one field": a sibling's outbox is read by its
    /// siblings as that sibling's own words, so a <c>TO: sibling:…</c> block filed there would put words in
    /// another session's mouth — and, authored <c>solo</c> in a file the writer is itself woken by, would
    /// start the writer's next turn on its own reply. A solo answers a sibling in its OWN outbox, as its role
    /// command says. One definition, read by the contract the session is shown and by the dispatcher that
    /// files the reply, so the two cannot disagree about what is addressable.
    /// </summary>
    public static IReadOnlyList<ITurnSource> Select_ReplyTargets(IReadOnlyList<ITurnSource> sources)
    {
        return [.. sources.Where(source => source.Kind != TurnSourceKinds.Sibling)];
    }

    static IReadOnlyList<ITurnSource> Resolve_WithSiblings(ISupervisionPaths paths, IOrchestrationSessionStore store, ITurnSource own, string orchId)
    {
        List<ITurnSource> sources = [own];

        var session = store.Get_Session_OrNull(orchId);

        // THE PAUSE GATE, and the two that come before it: nothing below runs for a solo that is closed,
        // unlinked or asleep — including the Load_All, so an ordinary solo costs one session read a tick.
        if (session == null || session.ClosedUtc != null || session.EndeavourId == null || session.Paused)
            return sources;

        // Case-insensitive for the reason the supervisor's dedupe gives: the thing being deduped is a FILE.
        HashSet<string> seenPaths = new([own.ChannelFilePath], StringComparer.OrdinalIgnoreCase);

        foreach (var sibling in EndeavourMembers_Resolver.Resolve_OpenSiblings(store.Load_All(), session).OrderBy(sibling => sibling.OrchId, StringComparer.Ordinal))
        {
            var outbox = paths.Get_SiblingOutboxFile(sibling.OrchId);

            if (!seenPaths.Add(outbox))
                continue;

            sources.Add(TurnSource_Factory.Create_Sibling(sibling.OrchId, outbox));
        }

        return sources;
    }

    /// <summary>
    /// The session's OWN channel — where its <c>turn_ended</c> record, its stall alert and any reply it
    /// addressed to nobody are written. For every role but the supervisor and a linked solo it is also its
    /// only source.
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
