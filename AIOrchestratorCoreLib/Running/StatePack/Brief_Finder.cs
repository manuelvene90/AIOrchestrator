using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;

namespace AIOrchestratorCoreLib.Running.StatePack;

/// <summary>
/// The entry a fresh member is working FROM. A session that resumes its transcript remembers its
/// brief; a fresh one does not, and the 4a design handed it "the last three entries" instead —
/// which do not contain the brief once a review round or two has passed. Measured 2026-09-08 on
/// the VPS: 3 of 25 member channels were compacted (45 entries ≈ 29 turns kept live) and the
/// archive was opened by a session twice in the whole window, so the brief of a long-lived member
/// was unreachable in practice. The finder runs on the WHOLE history (archive + live) and picks
/// deterministically, so the pack never depends on a model's guess about what it was asked.
///
/// <para>
/// Two rules, in order. A supervisor entry whose subject opens with a task marker is a brief — the
/// supervisors write `BRIEF —`, `REVIEW …`, `FINAL REVIEW —`, `FINDINGS —` by protocol, and the
/// LATEST such entry is the work in hand. When no subject carries a marker (an early orchestration,
/// a supervisor that words things its own way), the longest supervisor entry among the last
/// <see cref="FALLBACK_WINDOW"/> is taken: a brief is the one entry a supervisor cannot write
/// short. Null when the channel holds no supervisor entry at all.
/// </para>
/// </summary>
public static class Brief_Finder
{
    public const int FALLBACK_WINDOW = 20;

    public static readonly IReadOnlyList<string> TASK_MARKERS =
        ["BRIEF", "REVIEW", "FINAL REVIEW", "RE-REVIEW", "FINDINGS", "TASK", "GO AHEAD"];

    /// <summary>
    /// The types a typed entry may declare that make it the work in hand. Read BEFORE the subject
    /// markers, which is the whole of E3 requirement 3: a declared type is what the writer meant,
    /// while a subject beginning `BRIEF —` is a guess about what they meant.
    /// </summary>
    public static readonly IReadOnlyList<string> BRIEF_TYPES = ["brief", "review"];

    public static IChannelEntry? Find_OrNull(IReadOnlyList<IChannelEntry> history)
    {
        // RULE ZERO, AHEAD OF THE SUBJECT GUESS: a DECLARED type (E3 requirement 3, owner
        // 2026-09-10). The defect it removes is named in the brief — "a brief that is merely QUOTED
        // becomes the brief". A reviewer quoting a brief back writes a subject beginning `BRIEF —`,
        // because that is what they are quoting, and the marker rule below cannot tell the quotation
        // from the thing. A type is written by the tool from a flag the writer passed, so a quotation
        // carries the type of what it IS — a review, a report — not of what it mentions.
        //
        // IT DOES NOT REPLACE THE RULES BELOW, it precedes them. Every entry written before this
        // landed is untyped, and a session on the old skill goes on writing untyped entries; the
        // transition is the brief's own requirement. So an untyped history falls through to exactly
        // the behaviour it had.
        for (var i = history.Count - 1; i >= 0; i--)
        {
            var entry = history[i];

            if (entry.Author == ChannelAuthors.Supervisor
                && entry.Type != null
                && BRIEF_TYPES.Contains(entry.Type, StringComparer.OrdinalIgnoreCase))
                return entry;
        }

        for (var i = history.Count - 1; i >= 0; i--)
        {
            var entry = history[i];

            // AN ENTRY THAT DECLARED A DIFFERENT TYPE IS NOT A BRIEF, whatever its subject says.
            // This is the half that actually kills the quoted-brief defect: without it, a review
            // typed `review` but subjected `BRIEF — the parser fix` would still win here.
            if (entry.Type != null && !BRIEF_TYPES.Contains(entry.Type, StringComparer.OrdinalIgnoreCase))
                continue;

            if (entry.Author == ChannelAuthors.Supervisor && Has_TaskMarker(entry.Subject))
                return entry;
        }

        IChannelEntry? longest = null;
        var start = Math.Max(0, history.Count - FALLBACK_WINDOW);

        for (var i = start; i < history.Count; i++)
        {
            var entry = history[i];

            if (entry.Author != ChannelAuthors.Supervisor)
                continue;

            // ">=" so that, among equals, the LATER entry wins — recency is the tie-break because
            // the newer of two equally long supervisor entries is the more likely to be current.
            if (longest == null || entry.RawText.Length >= longest.RawText.Length)
                longest = entry;
        }

        return longest;
    }

    /// <summary>
    /// Whether ONE entry hands the member a NEW task — what retires its progress note
    /// (<see cref="StatePack_Locator.Archive_ProgressNote_IfNewTask"/>). Stricter than
    /// <see cref="Find_OrNull"/>: no "longest recent entry" fallback, which would retire a note on any
    /// long message; and not <c>GO AHEAD</c>, which says carry on with the task already in hand.
    /// </summary>
    public static bool Is_NewTask(IChannelEntry entry)
    {
        if (entry.Author != ChannelAuthors.Supervisor)
            return false;

        if (entry.Type != null)
            return BRIEF_TYPES.Contains(entry.Type, StringComparer.OrdinalIgnoreCase);

        return Has_TaskMarker(entry.Subject) && !entry.Subject.TrimStart().StartsWith(CARRY_ON_MARKER, StringComparison.OrdinalIgnoreCase);
    }

    const string CARRY_ON_MARKER = "GO AHEAD";

    public static bool Has_TaskMarker(string subject)
    {
        var trimmed = subject.TrimStart();

        foreach (var marker in TASK_MARKERS)
        {
            if (trimmed.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
