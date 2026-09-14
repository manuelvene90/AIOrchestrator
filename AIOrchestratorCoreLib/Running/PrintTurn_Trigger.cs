using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running.TurnCursor;

namespace AIOrchestratorCoreLib.Running;

/// <summary>
/// WHICH entries start a bridge-driven turn. An entry is inbound for a role when its author is someone
/// that role must answer — the supervisor (or the owner, typing straight into a spoke) for a member;
/// the owner for a solo and for the general supervisor; the owner AND every member for an orchestration
/// supervisor. A session's OWN entries never do, and neither do the app's: nudges, receipts and
/// <c>turn_ended</c> records are read on the next turn (the notes RIDE it — <see cref="Select_AgentNotes"/>),
/// and a turn started by the record of the previous turn would be a loop with one member in it.
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
/// </summary>
public static class PrintTurn_Trigger
{
    /// <summary>
    /// The inbound entries of one source not yet delivered, in the order they appear in the file. The
    /// caller merges the sources and decides the order between them.
    /// </summary>
    public static IReadOnlyList<IChannelEntry> Select_Pending(SessionRoles role, IReadOnlyList<IChannelEntry> entries, ITurnCursor cursor)
    {
        List<IChannelEntry> pending = [];

        foreach (var entry in entries)
        {
            if (!Is_Inbound(role, entry.Author))
                continue;

            if (cursor.Delivered.Contains(ChannelEntry_Digest.Compute(entry)))
                continue;

            pending.Add(entry);
        }

        return pending;
    }

    /// <summary>How far back an undelivered app note may still ride a turn — see <see cref="Select_AgentNotes"/>.</summary>
    public static readonly TimeSpan AGENT_NOTE_WINDOW = TimeSpan.FromHours(2);

    /// <summary>At most this many app notes ride one turn: the newest ones.</summary>
    public const int MAXIMUM_AGENT_NOTES = 5;

    /// <summary>
    /// AN APP NOTE FOR THE SESSION: written by the app, tagged for the agent alone, and not the
    /// dispatcher's own turn_ended record (which says what the session already knows).
    ///
    /// <para>
    /// THESE NEVER REACHED A BRIDGE-DRIVEN SESSION, and every one of them was written for it. A
    /// terminal session's watcher woke on any append, so an app note was read; a print or stream
    /// session reads only what its turn's prompt carries, and the prompt carried inbound entries
    /// alone (<see cref="Is_Inbound"/>). Measured 2026-09-11 in ai-orch-1: "the owner is still waiting
    /// for your reply", "PLAN.md is behind your verdicts", "HOLD — the owner has not answered", "the
    /// entry you just sent breaks the message contract" were all appended and none appeared in a turn.
    /// In fincanva-6 the same gap is why a supervisor re-asked a question the owner had already
    /// tapped: nothing it could read said so.
    /// </para>
    /// </summary>
    public static bool Is_AgentNote(IChannelEntry entry)
    {
        return entry.Author == ChannelAuthors.App
            && AppEntryAudience_Tag.Is_AgentTagged(entry.Subject)
            && !entry.Subject.Contains(PrintTurn_Words.TURN_ENDED_SUBJECT, StringComparison.Ordinal);
    }

    /// <summary>
    /// The app notes of the session's OWN channel that it has not been shown, to ride the turn about
    /// to start — never to start one. Only notes stamped within <see cref="AGENT_NOTE_WINDOW"/>, and
    /// only the newest <see cref="MAXIMUM_AGENT_NOTES"/>: the first turn after this shipped would
    /// otherwise have handed every session its channel's entire history of notes.
    /// </summary>
    public static IReadOnlyList<IChannelEntry> Select_AgentNotes(IReadOnlyList<IChannelEntry> entries, ITurnCursor cursor, DateTime nowLocal)
    {
        List<IChannelEntry> notes = [];

        foreach (var entry in entries)
        {
            if (!Is_AgentNote(entry))
                continue;

            if (cursor.Delivered.Contains(ChannelEntry_Digest.Compute(entry)))
                continue;

            // The app writes this stamp itself (ChannelAppender), so unlike an agent's it can be read.
            // One that cannot be parsed is not trusted to be recent.
            if (!DateTime.TryParseExact(entry.DateText, "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var stampedLocal))
                continue;

            if (nowLocal - stampedLocal > AGENT_NOTE_WINDOW)
                continue;

            notes.Add(entry);
        }

        return notes.Count <= MAXIMUM_AGENT_NOTES ? notes : [.. notes.Skip(notes.Count - MAXIMUM_AGENT_NOTES)];
    }

    public static bool Is_Inbound(SessionRoles role, ChannelAuthors author)
    {
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
