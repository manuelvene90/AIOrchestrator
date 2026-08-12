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

    /// <summary>
    /// The member declaring it has nothing owed and nothing running. Protocol vocabulary: the role
    /// commands instruct members to write this exact phrase when they go quiet on purpose.
    /// </summary>
    public const string STANDING_BY_MARKER = "STANDING BY";

    /// <summary>
    /// Every phrase this resolver acts on. Exists so a test can walk the vocabulary instead of a
    /// list someone must remember to extend — the role commands and this file live apart, and three
    /// of one night's findings were the docs teaching something the matcher does not do.
    /// </summary>
    public static readonly IReadOnlyList<string> ALL_MARKERS =
    [
        WRITING_WINDOW_OPEN_MARKER,
        WRITING_WINDOW_CLOSED_MARKER,
        MUTATION_WINDOW_OPEN_MARKER,
        MUTATION_WINDOW_CLOSED_MARKER,
        BLOCKED_ON_OWNER_MARKER,
        STANDING_BY_MARKER,
    ];

    /// <summary>The two that END a window, which the close-scan treats differently on purpose.</summary>
    public static readonly IReadOnlyList<string> CLOSING_MARKERS =
    [
        WRITING_WINDOW_CLOSED_MARKER,
        MUTATION_WINDOW_CLOSED_MARKER,
    ];

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

        // CHECKED BEFORE THE AWAITING-VERDICT TEST, and gated on the member the same way BLOCKED ON
        // OWNER is. Both sides of this conflict were mine and both are kept: the awaiting-verdict
        // predicate is the correct fallback, and it is exactly what made an idle member look like a
        // pending review — "standing by, nothing owed" put the supervisor on the hook for a verdict
        // nobody asked for, and it was nudged for not giving one.
        //
        // Order matters and is not cosmetic: a declaration IS a member-last entry, so the fallback
        // would swallow it if it ran first.
        //
        // AND THE DECLARATION MUST HAVE NOTHING BEHIND IT. "standing by, nothing filed" and "filed,
        // standing by, awaiting a verdict" were one value, and every consumer inherited the collapse:
        // the nudge went silent on a member waiting for a verdict, and the owner's topic read that
        // member as `idle` — which the builder's own docstring forbids three lines above the code
        // that produced it. Split HERE, once, rather than in each consumer: two surfaces answering
        // the same question apart is how they start disagreeing about the same member.
        //
        // Both halves of this are load-bearing. Without Is_PureDeclaration inside the predicate the
        // gate is self-defeating — a declaration is itself a member entry after the supervisor's, so
        // the predicate would be true for every declaration and this branch would be dead code.
        if (ChannelAuthor_Kinds.Is_Member(lastEntry.Author)
            && Contains_Marker(lastEntry, STANDING_BY_MARKER)
            && !Is_AwaitingVerdict(entries))
            return MemberStates.StandingBy;

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
            if (!ChannelAuthor_Kinds.Is_Member(entries[position].Author))
                continue;

            // A boot announcement is a hello and a PURE DECLARATION is an acknowledgement —
            // neither is filed work, and neither can put a supervisor on the hook for a verdict.
            // A report that merely ENDS with the marker is still a report: the subject decides,
            // which is how members actually distinguish the two.
            if (Is_BootAnnouncement(entries[position]) || Is_PureDeclaration(entries[position]))
                continue;

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
    /// An entry whose SUBJECT is the standing-by declaration — an acknowledgement rather than filed
    /// work.
    ///
    /// The distinction is the subject, because that is where members put the thing they are actually
    /// saying: `STANDING BY — waiting for a brief` is a declaration, while
    /// `call pinned: d899c7e, 635 tests` with the marker in its closing line is a REPORT that happens
    /// to end by going quiet. Collapsing the two is what silenced the supervisor's reminder for a
    /// member idle BECAUSE it was waiting on a verdict — five times in one day, caught only because
    /// the running build predates the marker.
    /// </summary>
    static bool Is_PureDeclaration(IChannelEntry entry)
    {
        return Contains_MarkerToken(entry.Subject, STANDING_BY_MARKER);
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
    /// <summary>
    /// The member's CURRENT BRIEF — the last thing the supervisor said to it. THE one place this is
    /// answered: it had three implementations, in the card builder, the status line and here, all
    /// agreeing today and none joined to the others. The duration beside it already went through a
    /// single formatter; the question of WHICH ENTRY did not.
    /// </summary>
    public static IChannelEntry? Find_LastBrief_OrNull(IReadOnlyList<IChannelEntry> entries)
    {
        for (var index = entries.Count - 1; index >= 0; index--)
        {
            if (entries[index].Author == ChannelAuthors.Supervisor)
                return entries[index];
        }

        return null;
    }

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

        // THE CLOSE SCAN ACCEPTS AN UNCERTAIN AUTHOR, and the asymmetry is the whole point.
        //
        // A missed close pins a member in this state FOREVER — the app then tells it to append the
        // close it already appended — while a spurious close merely ends a window early, which is
        // noise the member undoes by declaring again. So ambiguity resolves toward NOT-open on both
        // scans: strictly for the open, leniently for the close.
        //
        // Only UNKNOWN is accepted, never a supervisor: an unrecognised author word may well BE the
        // member (a drifted header), whereas a supervisor is known not to be.
        var lastClosed = Find_LastEntryIndex_WithMarker(entries, closedMarker, acceptUncertainAuthor: true);

        return lastClosed < lastOpen;
    }

    /// <summary>
    /// MEMBER-AUTHORED ENTRIES ONLY, and this is the cheaper half of the whole marker fix.
    ///
    /// A window is a statement about what the MEMBER is doing, so only the member can make it. This
    /// scan used to accept any author, which is how a SUPERVISOR's brief — one warning a reviewer
    /// about this very bug — opened that reviewer's window and pinned it in WritingWindowOpen for
    /// four hours. The same file already gates BLOCKED ON OWNER and STANDING BY this way, because
    /// they are only ever read off the member's own last entry. All six markers are member
    /// vocabulary; the window pair was the one that had no gate.
    ///
    /// This kills the entire supervisor-discussion class on its own, independently of any anchoring.
    /// </summary>
    static int Find_LastEntryIndex_WithMarker(IReadOnlyList<IChannelEntry> entries, string marker, bool acceptUncertainAuthor = false)
    {
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            if (!Can_AnnounceAWindow(entries[i].Author, acceptUncertainAuthor))
                continue;

            if (Contains_Marker(entries[i], marker))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Only a session that can WRITE can announce a writing window — so a REVIEWER never can. It is
    /// launched without Write, Edit and NotebookEdit, and a hook blocks mutating shell commands.
    ///
    /// This is a root fix for a trap, not a tidy-up. A reviewer filing a finding ABOUT a window used
    /// to pin itself in <see cref="MemberStates.WritingWindowOpen"/> with NO EXIT: the window test
    /// runs before the last-entry rules, so its own declaration cannot clear it; `reviewer.md` never
    /// taught the close marker at all; and once window markers became member-gated, a supervisor
    /// could no longer clear it either. The session was stuck until somebody killed it — and
    /// reviewers are the members most likely to write about markers, because reviewing this
    /// vocabulary is their job.
    ///
    /// Making the state UNREACHABLE for them closes it by construction, which no wording in a doc
    /// can do.
    /// </summary>
    static bool Can_AnnounceAWindow(ChannelAuthors author, bool acceptUncertainAuthor)
    {
        if (acceptUncertainAuthor && author == ChannelAuthors.Unknown)
            return true;

        return author == ChannelAuthors.Implementer || author == ChannelAuthors.Solo;
    }

    /// <summary>
    /// TWO RULES, because the subject and the body are different kinds of text and one rule measured
    /// badly on both.
    ///
    /// BODY: line-start anchoring. A body is prose, and a marker inside a sentence is discussion —
    /// which is how a brief WARNING a reviewer about this bug pinned it for four hours.
    ///
    /// SUBJECT: token match anywhere, word boundary either side. A subject is one authored summary
    /// field, not prose, and the positional argument does not transfer to it. Measured rather than
    /// argued: against all 95 marker-bearing subjects on this machine, start-anchoring the subject
    /// lost 10 GENUINE declarations to catch 1 discussion. The ten are ordinary house style —
    /// `TASK 1 committed d123150. WRITING WINDOW OPEN for the hardening` — plus the compound
    /// `WRITING + MUTATION WINDOW OPEN`, which misses both markers at once.
    ///
    /// AND THE SAFE DIRECTION INVERTS FOR THE CLOSED MARKERS, which is why that trade was not merely
    /// unprofitable but wrong. <see cref="Has_OpenWindow"/> reads a missing close as still-open, so a
    /// CLOSED marker that stops counting pins the member in WritingWindowOpen — the exact four-hour
    /// failure this rule exists to prevent, reached through the other door. The corpus contained a
    /// live one: `A3 COMPLETE · … · MUTATION WINDOW CLOSED`.
    ///
    /// Blockquotes are not stripped in the body, and that is the point: `&gt; STANDING BY` is
    /// quotation by definition. Bullets and bold are stripped because `**STANDING BY** — waiting` is
    /// a declaration written by someone using markdown.
    /// </summary>
    static bool Contains_Marker(IChannelEntry entry, string marker)
    {
        if (Contains_MarkerToken(entry.Subject, marker))
            return true;

        foreach (var rawLine in entry.Body.Split('\n'))
        {
            if (Declares_Marker(rawLine, marker))
                return true;
        }

        return false;
    }

    /// <summary>Quotation, as opposed to decoration. A line that opens with one of these is
    /// reporting what somebody said, which is the whole class being excluded.</summary>
    static readonly char[] QUOTATION_OPENERS = ['>', '"', '\''];

    /// <summary>
    /// The marker as a whole TOKEN — a letter or digit on either side means this is part of a longer
    /// word, not the marker. Every occurrence is tried, not just the first: a subject can name a
    /// result before its marker, which is the house style the position rule lost.
    /// </summary>
    static bool Contains_MarkerToken(string text, string marker)
    {
        var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

        while (index >= 0)
        {
            var before = index == 0 ? ' ' : text[index - 1];
            var afterIndex = index + marker.Length;

            var startsCleanly = !char.IsLetterOrDigit(before);
            var endsCleanly = afterIndex >= text.Length || !char.IsLetterOrDigit(text[afterIndex]);

            // QUOTATION IS EXCLUDED HERE TOO. A body line refuses a leading >, " or ' — and moving the
            // subject onto a token match silently dropped that, so the exact quoted string the suite
            // FORBIDS in a body was declaring in a subject. Same rule, both fields, one list.
            var isQuoted = Array.IndexOf(QUOTATION_OPENERS, before) >= 0;

            if (startsCleanly && endsCleanly && !isQuoted)
                return true;

            index = text.IndexOf(marker, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    static bool Declares_Marker(string rawLine, string marker)
    {
        var start = 0;

        // Skip DECORATION — bullets, bold, emoji, whatever the writer put in front. The strip is by
        // CATEGORY rather than a list of characters, because the list is unguessable: it was written
        // as *_#- and then met "🚩 BLOCKED ON OWNER", which is how members actually flag things.
        while (start < rawLine.Length && !char.IsLetterOrDigit(rawLine[start]))
        {
            if (Array.IndexOf(QUOTATION_OPENERS, rawLine[start]) >= 0)
                return false;

            start++;
        }

        var line = rawLine.Substring(start);

        if (!line.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            return false;

        var rest = line.Substring(marker.Length);

        // Nothing after it, or a separator. A letter or digit means this is a LONGER token that
        // merely starts the same way, not the marker.
        return rest.Length == 0 || !char.IsLetterOrDigit(rest[0]);
    }
}
