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

    /// <summary>
    /// A marker DECLARES only when it begins a line. It used to be a bare substring anywhere in the
    /// entry, which meant discussing the vocabulary set the state — and these are the sessions
    /// instructed to discuss the vocabulary. A supervisor's brief warning a reviewer about this very
    /// bug contained the phrase, and pinned that reviewer in WritingWindowOpen for four hours.
    ///
    /// The direction of failure is deliberate. Anchoring can only make a marker count for LESS, and
    /// for both markers less means MORE waking: a missed window makes a member look dormant, a missed
    /// declaration makes it look like it never declared. Both end in a nudge, which is noise. The
    /// opposite error is silence — a false StandingBy disarms the orphan recovery that is the app's
    /// only proof a monitor is dead, and nothing then detects the session at all.
    ///
    /// Blockquotes are NOT stripped, and that is the point of the exclusion: `&gt; STANDING BY` is
    /// quotation by definition, which is exactly the class being excluded. Bullets and bold are
    /// stripped because `**STANDING BY** — waiting on rev-3` is a declaration written by someone
    /// using markdown.
    /// </summary>
    static bool Contains_Marker(IChannelEntry entry, string marker)
    {
        // THE SUBJECT COUNTS, and it is where members actually put these — the role commands say to
        // append "an entry containing exactly WRITING WINDOW OPEN naming the files in flight", and in
        // practice that lands in the header. Anchored to the START of the subject, so a subject
        // ABOUT a marker ("the STANDING BY marker is broken") is not a declaration of one.
        //
        // Read as a parsed FIELD rather than by scanning the raw header line: the header is a single
        // line, so anchoring to line starts alone would silently stop recognising every marker ever
        // written in a subject. Four existing tests caught exactly that.
        if (Declares_Marker(entry.Subject, marker))
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
