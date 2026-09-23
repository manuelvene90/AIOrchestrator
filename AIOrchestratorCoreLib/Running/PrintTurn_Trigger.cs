using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running.TurnCursor;
using AIOrchestratorCoreLib.Running.TurnSource;

namespace AIOrchestratorCoreLib.Running;

/// <summary>
/// WHICH entries start a bridge-driven turn. An entry is inbound for a role when its author is someone
/// that role must answer — the supervisor (or the owner, typing straight into a spoke) for a member;
/// the owner for a solo and for the general supervisor; the owner AND every member for an orchestration
/// supervisor. A session's OWN entries never do, and neither do the app's: nudges, receipts and
/// <c>turn_ended</c> records are read on the next turn, and a turn started by the record of the previous
/// turn would be a loop with one member in it.
///
/// <para>
/// THE SUPERVISOR'S MEMBER CLAUSE WAS WRITTEN BEFORE ANYTHING COULD REACH IT. Until the bridge learned
/// to watch more than one channel per session, a supervisor's only source was <c>owner-channel.md</c>,
/// where no member ever writes — so <c>Is_Inbound(Supervisor, Implementer)</c> was true and unreachable,
/// and the role command was told to <c>cat</c> its spokes at the end of every turn to make up for it.
/// It is reachable now: the dispatcher asks this question once per source.
/// </para>
/// <para>
/// PENDING IS DECIDED BY IDENTITY, NOT BY INDEX. The cursor holds the identities of the entries already
/// delivered (<see cref="Channels.ChannelEntry_Digest"/>); an inbound entry whose identity is not among
/// them is pending. The <c>[n]</c> in a header is agent-written and has duplicated in production
/// (CLAUDE.md decision 12) — under an index cursor a member's filed report arriving with a repeated or
/// lower index would never be handed to its supervisor, and nothing would say so. Counting entries or
/// using their position is worse still: compaction breaks both (decision 13).
/// </para>
/// <para>
/// AND IT DEPENDS ON THE KIND OF CHANNEL, since a solo got a second one (sibling plan 2026-09-23, spec §5.4).
/// On a <see cref="TurnSourceKinds.Sibling"/> source — another solo's outbox — an entry authored
/// <c>solo</c> is inbound for a solo, because the reader never writes that file: it is the sibling speaking.
/// On the owner channel the same author word is still the reader's own record and still wakes nothing. The
/// two-argument overloads answer for an owner or spoke source, which is every source that existed before
/// siblings, so their callers did not move.
/// </para>
/// </summary>
public static class PrintTurn_Trigger
{
    /// <summary>
    /// The inbound entries of one source not yet delivered, in the order they appear in the file. The
    /// caller merges the sources and decides the order between them.
    /// </summary>
    public static IReadOnlyList<IChannelEntry> Select_Pending(SessionRoles role, IReadOnlyList<IChannelEntry> entries, ITurnCursor cursor)
    {
        return Select_Pending(role, TurnSourceKinds.Owner, entries, cursor);
    }

    /// <summary>The same, for a source of the given kind — the dispatcher's call, once per source.</summary>
    public static IReadOnlyList<IChannelEntry> Select_Pending(SessionRoles role, TurnSourceKinds kind, IReadOnlyList<IChannelEntry> entries, ITurnCursor cursor)
    {
        List<IChannelEntry> pending = [];

        foreach (var entry in entries)
        {
            if (!Is_Inbound(role, kind, entry.Author))
                continue;

            if (cursor.Delivered.Contains(ChannelEntry_Digest.Compute(entry)))
                continue;

            pending.Add(entry);
        }

        return pending;
    }

    public static bool Is_Inbound(SessionRoles role, ChannelAuthors author)
    {
        return Is_Inbound(role, TurnSourceKinds.Owner, author);
    }

    public static bool Is_Inbound(SessionRoles role, TurnSourceKinds kind, ChannelAuthors author)
    {
        // A SIBLING'S OUTBOX HAS ONE WRITER, the sibling, and one kind of reader, a solo. Anything else found
        // there — an app line, an owner's hand edit — is not a sibling speaking, so it wakes nobody.
        if (kind == TurnSourceKinds.Sibling)
            return role == SessionRoles.Solo && author == ChannelAuthors.Solo;

        return role switch
        {
            SessionRoles.Implementer or SessionRoles.Reviewer => author is ChannelAuthors.Supervisor or ChannelAuthors.Owner,
            SessionRoles.Solo or SessionRoles.General => author == ChannelAuthors.Owner,
            SessionRoles.Supervisor => author == ChannelAuthors.Owner || ChannelAuthor_Kinds.Is_Member(author),
            SessionRoles.Communicator => author == ChannelAuthors.Owner,
            _ => false,
        };
    }
}
