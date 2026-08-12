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

        var lastEntry = entries[entries.Count - 1];

        if (ChannelAuthor_Kinds.Is_Member(lastEntry.Author))
        {
            if (Contains_Marker(lastEntry, BLOCKED_ON_OWNER_MARKER))
                return MemberStates.BlockedOnOwner;

            // Checked BEFORE the fallback, because the fallback is what made an idle member look
            // like a pending review: every member-last entry resolved to AwaitingSupervisorReview,
            // so "standing by, nothing owed" put the supervisor on the hook for a verdict that was
            // never requested — and it was nudged for not giving one.
            if (Contains_Marker(lastEntry, STANDING_BY_MARKER))
                return MemberStates.StandingBy;

            return MemberStates.AwaitingSupervisorReview;
        }

        return MemberStates.ImplementerWorking;
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
