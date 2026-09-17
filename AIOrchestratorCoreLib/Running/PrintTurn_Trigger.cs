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
            if (!Is_Inbound(role, entry))
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
            && !entry.Subject.Contains(PrintTurn_Words.TURN_ENDED_SUBJECT, StringComparison.Ordinal)
            // A ROUTED REPORT IS A BRIEF, NOT A NOTE. It is agent-tagged because the owner cannot act
            // on it (decision 15), and without this clause it would be a riding note AND inbound at
            // once — two answers to "does this start a turn" in one expression.
            && !RoutedReport_Tag.Is_Routed(entry.Subject);
    }

    /// <summary>
    /// The app notes this session has not been shown, to ride the turn about to start — never to
    /// start one. Only notes stamped within <see cref="AGENT_NOTE_WINDOW"/>, and only the newest
    /// <see cref="MAXIMUM_AGENT_NOTES"/>: the first turn after this shipped would otherwise have
    /// handed every session its channel's entire history of notes.
    ///
    /// <para>
    /// TWO FILES SINCE 2026-09-15, ONE RULE. A note used to be an entry in the session's own channel
    /// and nothing else; plan 02 routes the bookkeeping kinds to
    /// <see cref="Channels.StatusLog.StatusLog_Store"/>, whose records ARE channel entries, and the
    /// caller hands both lists here together. The window and the cap therefore apply to the MERGED
    /// set, which is the point: five notes total, not five per file.
    /// </para>
    /// <para>
    /// THE DELIVERED TEST IS A PREDICATE RATHER THAN A CURSOR, and that is the only reason the
    /// signature changed. There are two cursors now — the session's own channel and
    /// <see cref="Channels.StatusLog.StatusLog_Store.CURSOR_KEY"/> — and an entry is already
    /// delivered if EITHER says so. Passing one cursor would have forced this method to know which
    /// list an entry came from, which is exactly the knowledge it must not need.
    /// </para>
    /// <para>
    /// "THE NEWEST FIVE" IS BY STAMP, NOT BY POSITION, and that is not a nicety once there are two
    /// lists: concatenating a channel and a log puts every log note behind every channel note, so a
    /// tail-of-the-list cap would drop a note written a minute ago in favour of one written two
    /// hours ago purely because of which file it landed in. One list in, sorted once, capped once.
    /// The sort is STABLE, so a single source whose stamps are already in order is handed back in
    /// exactly the order it arrived — which is what every caller before 2026-09-15 relied on.
    /// </para>
    /// </summary>
    public static IReadOnlyList<IChannelEntry> Select_AgentNotes(
        IReadOnlyList<IChannelEntry> entries,
        Func<IChannelEntry, bool> alreadyDelivered,
        DateTime nowLocal)
    {
        List<(IChannelEntry Entry, DateTime StampedLocal)> notes = [];

        foreach (var entry in entries)
        {
            if (!Is_AgentNote(entry))
                continue;

            if (alreadyDelivered(entry))
                continue;

            // The app writes this stamp itself (ChannelAppender, StatusLog_Store), so unlike an
            // agent's it can be read. One that cannot be parsed is not trusted to be recent.
            if (!DateTime.TryParseExact(entry.DateText, "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var stampedLocal))
                continue;

            if (nowLocal - stampedLocal > AGENT_NOTE_WINDOW)
                continue;

            notes.Add((entry, stampedLocal));
        }

        var ordered = notes.OrderBy(note => note.StampedLocal).Select(note => note.Entry).ToList();

        return ordered.Count <= MAXIMUM_AGENT_NOTES ? ordered : [.. ordered.Skip(ordered.Count - MAXIMUM_AGENT_NOTES)];
    }

    /// <summary>
    /// WHETHER THIS WHOLE ENTRY IS INBOUND — the author's answer, plus the one exception that cannot
    /// be read from an author: a ROUTED REPORT (<see cref="Channels.RoutedReport_Tag"/>), which the
    /// app writes into a reviewer's channel when an implementer files the fix a re-review contract
    /// was waiting for.
    ///
    /// <para>
    /// A SECOND OVERLOAD RATHER THAN A WIDER FIRST ONE, deliberately. The author-only test is the one
    /// <see cref="PendingTraffic.WakeUp_Policy"/>'s docstring quotes, and
    /// <c>WakeUpPolicyTests.TheAppsOwnEntries_AreInboundForNobody</c> pins it for every role so that
    /// "a later change cannot quietly make app traffic a reason to wake the most expensive role in
    /// the system". That guard is still exactly true. What changed is that ONE app entry in a
    /// reviewer's channel is now a brief, and it is recognisable only by its subject.
    /// </para>
    /// <para>
    /// AND THE HUB-AND-SPOKE TOPOLOGY IS UNTOUCHED (CLAUDE.md decision 4). The relay is written by
    /// the APP into the reviewer's OWN channel. No member reads another member's file, and the author
    /// screen below means a member cannot forge one by writing the tag itself.
    /// </para>
    /// </summary>
    public static bool Is_Inbound(SessionRoles role, IChannelEntry entry)
    {
        return Is_Inbound(role, entry.Author) || Is_RoutedReport(role, entry);
    }

    /// <summary>
    /// The four screens, all required: written by the app, tagged by the relay at the FRONT of the
    /// subject, and read by a REVIEWER. Nothing else about <see cref="ChannelAuthors.App"/> moves.
    /// </summary>
    static bool Is_RoutedReport(SessionRoles role, IChannelEntry entry)
    {
        return role == SessionRoles.Reviewer
            && entry.Author == ChannelAuthors.App
            && RoutedReport_Tag.Is_Routed(entry.Subject);
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
