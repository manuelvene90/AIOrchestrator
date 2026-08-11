using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;

namespace AIOrchestratorCoreLib.Status;

/// <summary>
/// Derives a spoke's development state from its parsed channel entries. The window and blocked
/// markers are protocol vocabulary — the role commands instruct agents to use these exact phrases.
/// </summary>
public static class MemberState_Resolver
{
    public const string WRITING_WINDOW_OPEN_MARKER = "WRITING WINDOW OPEN";
    public const string WRITING_WINDOW_CLOSED_MARKER = "WRITING WINDOW CLOSED";
    public const string MUTATION_WINDOW_OPEN_MARKER = "MUTATION WINDOW OPEN";
    public const string MUTATION_WINDOW_CLOSED_MARKER = "MUTATION WINDOW CLOSED";
    public const string BLOCKED_ON_OWNER_MARKER = "BLOCKED ON OWNER";

    /// <summary>The second word of the boot subject every member is required to write: "imp-1 online".</summary>
    public const string BOOT_ANNOUNCEMENT_WORD = "online";

    public static MemberStates Resolve(IReadOnlyList<IChannelEntry> entries)
    {
        if (entries.Count == 0)
            return MemberStates.NewNoTraffic;

        if (Has_OpenWindow(entries, WRITING_WINDOW_OPEN_MARKER, WRITING_WINDOW_CLOSED_MARKER))
            return MemberStates.WritingWindowOpen;

        if (Has_OpenWindow(entries, MUTATION_WINDOW_OPEN_MARKER, MUTATION_WINDOW_CLOSED_MARKER))
            return MemberStates.WritingWindowOpen;

        var lastEntry = Find_LastConversationEntry_OrNull(entries);

        if (lastEntry == null)
            return MemberStates.ImplementerWorking;

        // BLOCKED ON OWNER stands on its own and is deliberately NOT routed through the
        // awaiting-verdict test below: a member waiting on the owner is blocked whether or not it
        // has ever been briefed, and it is waiting on someone else either way.
        if (ChannelAuthor_Kinds.Is_Member(lastEntry.Author) && Contains_Marker(lastEntry, BLOCKED_ON_OWNER_MARKER))
            return MemberStates.BlockedOnOwner;

        if (Is_AwaitingVerdict(entries))
            return MemberStates.AwaitingSupervisorReview;

        return MemberStates.ImplementerWorking;
    }

    /// <summary>
    /// Has this member FILED WORK and is now waiting on a verdict?
    ///
    /// Not "did it speak last", which is what this used to ask and is not the same question. The
    /// boot protocol makes a member speak FIRST: implementer.md and reviewer.md both mandate an
    /// "&lt;id&gt; online" entry as its opening act, so every channel begins
    /// `[1] imp-1 online` → `[2] BRIEF`. Under "spoke last" that made a BRIEF look like a verdict —
    /// the ledger armed on briefing, which is the exact regression it was meant to kill — and it
    /// published a member that had only said "online" as awaiting review, which let the
    /// awaiting-answer hook permit BRIEFING it during an open question. Both consumers inherited one
    /// wrong answer.
    ///
    /// **Since the supervisor LAST SPOKE in this channel, has the member filed at least one entry
    /// that is not merely its boot announcement?**
    ///
    /// Anchored to a POSITION, not to existence. The first version of this asked whether a
    /// supervisor entry existed ANYWHERE, which is permanently true from entry [2] onward — so from
    /// that moment it silently became the "spoke last" rule it was written to replace, and only a
    /// member's very first boot still answered correctly. Our own lifecycle then made the failure
    /// routine: resume is a fresh role-command re-entry, so a settled channel reads
    /// `verdict → imp-1 online` after any restart, and the member was published as awaiting a
    /// verdict while it was waiting for WORK.
    ///
    /// The same sentence decides the ledger too, from the other side: judged AT a verdict, the
    /// entries before it show work filed since the brief, so the ledger arms — and the same channel
    /// read afterwards shows the member no longer waiting.
    /// </summary>
    public static bool Is_AwaitingVerdict(IReadOnlyList<IChannelEntry> entries)
    {
        var lastSupervisorPosition = -1;

        for (var position = entries.Count - 1; position >= 0; position--)
        {
            if (entries[position].Author != ChannelAuthors.Supervisor)
                continue;

            lastSupervisorPosition = position;
            break;
        }

        // The supervisor has never spoken here, so nothing has been asked of this member and it
        // cannot be waiting for an answer.
        if (lastSupervisorPosition < 0)
            return false;

        for (var position = lastSupervisorPosition + 1; position < entries.Count; position++)
        {
            if (ChannelAuthor_Kinds.Is_Member(entries[position].Author) && !Is_BootAnnouncement(entries[position]))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The "&lt;id&gt; online" entry a member writes as its FIRST act on every boot. It is protocol
    /// vocabulary, not prose — implementer.md and reviewer.md mandate that subject verbatim — which
    /// is what makes matching it legitimate here, in the same way the window markers are matched.
    ///
    /// It has to be excluded because our lifecycle repeats it: resume is a fresh role-command
    /// re-entry for every role, so after any restart or respawn a settled channel reads
    /// `[4] verdict → [5] imp-1 online`, and counting that hello as filed work published the member
    /// as awaiting a verdict while it was actually waiting for WORK.
    ///
    /// Matched as ONE token then the word "online", so a genuine report titled "the server is back
    /// online" is not swallowed by it.
    /// </summary>
    static bool Is_BootAnnouncement(IChannelEntry entry)
    {
        var words = entry.Subject.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return words.Length == 2 && words[1].Equals(BOOT_ANNOUNCEMENT_WORD, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// WHO SPOKE LAST, ignoring the app. The app is not a participant in this conversation — its
    /// nudges and resume notices are about the conversation, not part of it — and it writes into a
    /// channel precisely when someone has been waiting, so counting it inverted the answer on the
    /// most common path: a member filed a report, the app nudged the supervisor about it, and the
    /// member instantly stopped reading as "awaiting review". That mislabelled the status line and
    /// made the idle-nudge logic nudge a member for waiting on a reply it had already asked for.
    ///
    /// This is the ONE place the question is answered. The turn-end ledger trigger and the published
    /// awaiting-verdict list both come through here rather than each deciding for themselves.
    /// </summary>
    public static IChannelEntry? Find_LastConversationEntry_OrNull(IReadOnlyList<IChannelEntry> entries)
    {
        for (var index = entries.Count - 1; index >= 0; index--)
        {
            if (entries[index].Author != ChannelAuthors.App)
                return entries[index];
        }

        return null;
    }

    static bool Has_OpenWindow(IReadOnlyList<IChannelEntry> entries, string openMarker, string closedMarker)
    {
        var lastOpen = Find_LastEntryIndex_WithMarker(entries, openMarker);

        if (lastOpen < 0)
            return false;

        var lastClosed = Find_LastEntryIndex_WithMarker(entries, closedMarker);

        return lastClosed < lastOpen;
    }

    static int Find_LastEntryIndex_WithMarker(IReadOnlyList<IChannelEntry> entries, string marker)
    {
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            if (Contains_Marker(entries[i], marker))
                return i;
        }

        return -1;
    }

    static bool Contains_Marker(IChannelEntry entry, string marker)
    {
        return entry.RawText.Contains(marker, StringComparison.OrdinalIgnoreCase);
    }
}
