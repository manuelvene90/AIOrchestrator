using System.Text;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Planning.PlanProgress;

namespace AIOrchestratorCoreLib.Bridge.Siblings;

/// <summary>
/// <c>&lt;orch&gt;/ENDEAVOUR.md</c> — WHAT THE OTHER SIBLINGS ARE DOING, for a linked solo to read at every
/// boundary (spec 2026-09-23 §3.4, §5.1). Built in C# from the parsers that already exist, so bash never
/// parses markdown and no fact gets a second spelling (decision 12): the bar is
/// <see cref="PlanProgress_Formatter.Describe_Counts(IPlanProgress)"/>, the ledger is
/// <see cref="PlanLedger_Parser"/>'s, the history spans live file and archive (decision 13).
///
/// <para>
/// BOUNDED BY CONSTRUCTION, because a solo pays for every byte of it in context at every boundary: at most
/// <see cref="MAX_UNFINISHED_LINES"/> ledger lines, <see cref="MAX_OWNER_ENTRIES"/> owner-channel entries of
/// at most <see cref="MAX_ENTRY_BODY_CHARS"/> body characters, and <see cref="MAX_OUTBOX_SUBJECTS"/> outbox
/// subjects per sibling — about 1.5k tokens a sibling (§5.1 "Cost").
/// </para>
/// <para>
/// NO CLOCK IN THE TEXT (the <see cref="AIOrchestratorCoreLib.Telegram.GeneralDashboard_Composer"/> rule). The file is rewritten only
/// when its text changes, so a timestamp would make every tick a change. No entry dates either: freshness is
/// the content, which is what moves.
/// </para>
/// <para>
/// APP ENTRIES ARE NEVER SHOWN, of either audience: only <see cref="ChannelAuthors.Owner"/> and
/// <see cref="ChannelAuthors.Solo"/> pass — what the owner asked and what the sibling answered, not the app's
/// housekeeping about it.
/// </para>
/// </summary>
public static class EndeavourDigest_Builder
{
    public const int MAX_UNFINISHED_LINES = 15;
    public const int MAX_OWNER_ENTRIES = 6;
    public const int MAX_ENTRY_BODY_CHARS = 400;
    public const int MAX_OUTBOX_SUBJECTS = 3;

    const string CUT_MARK = "…";

    /// <summary>What is still owed, in the ledger's own markers: open, in progress, blocked, blocked on the owner.</summary>
    static readonly HashSet<string> UNFINISHED_MARKERS =
    [
        PlanLedger_Markers.OPEN,
        PlanLedger_Markers.IN_PROGRESS,
        PlanLedger_Markers.BLOCKED,
        PlanLedger_Markers.BLOCKED_ON_OWNER,
    ];

    public static string Build(IReadOnlyList<SiblingDigestInput> siblings)
    {
        var text = new StringBuilder();

        text.Append("# Your open siblings\n\n")
            .Append("Written by the app from each sibling's PLAN.md, owner channel and outbox, and rewritten only when that changes. ")
            .Append("Read-only: nothing you write here reaches anyone — talk to a sibling through your own outbox.\n");

        foreach (var sibling in siblings)
            Append_Block(text, sibling);

        return text.ToString();
    }

    static void Append_Block(StringBuilder text, SiblingDigestInput sibling)
    {
        text.Append('\n')
            .Append($"## {sibling.Name} ({sibling.OrchId}) · {sibling.WorkingPath} · branch {sibling.Branch ?? "?"} · {(sibling.Paused ? "paused" : "live")}\n")
            .Append(sibling.Progress == null ? "no task ledger yet" : PlanProgress_Formatter.Describe_Counts(sibling.Progress))
            .Append('\n');

        Append_Unfinished(text, sibling.Progress);
        Append_OwnerChannel(text, sibling.OwnerChannelTail);
        Append_Outbox(text, sibling.OutboxSubjects);
    }

    static void Append_Unfinished(StringBuilder text, IPlanProgress? progress)
    {
        if (progress == null)
            return;

        var unfinished = progress.Lines.Where(line => UNFINISHED_MARKERS.Contains(line.Marker)).ToList();

        if (unfinished.Count == 0)
            return;

        text.Append("\nunfinished:\n");

        foreach (var line in unfinished.Take(MAX_UNFINISHED_LINES))
            text.Append(line.IsSubTask ? "  " : "").Append($"[{line.Marker}] {line.Text}\n");

        if (unfinished.Count > MAX_UNFINISHED_LINES)
            text.Append($"+{unfinished.Count - MAX_UNFINISHED_LINES} more\n");
    }

    static void Append_OwnerChannel(StringBuilder text, IReadOnlyList<IChannelEntry> history)
    {
        var shown = history
            .Where(entry => entry.Author is ChannelAuthors.Owner or ChannelAuthors.Solo)
            .TakeLast(MAX_OWNER_ENTRIES)
            .ToList();

        if (shown.Count == 0)
            return;

        text.Append("\nowner channel:\n");

        foreach (var entry in shown)
        {
            text.Append($"[{entry.Index}] FROM {ChannelAuthor_Words.Get_Word(entry.Author)} — {entry.Subject}\n");

            var body = Cut(entry.Body.Trim());

            // QUOTED, so a body line that looks like a heading or an entry header cannot pass for one.
            foreach (var line in body.Split('\n'))
                text.Append("> ").Append(line.TrimEnd('\r')).Append('\n');
        }
    }

    static void Append_Outbox(StringBuilder text, IReadOnlyList<string> subjects)
    {
        text.Append("\noutbox:");

        if (subjects.Count == 0)
        {
            text.Append(" nothing written yet\n");
            return;
        }

        text.Append('\n');

        foreach (var subject in subjects.TakeLast(MAX_OUTBOX_SUBJECTS))
            text.Append($"- {subject}\n");
    }

    static string Cut(string body)
    {
        if (body.Length <= MAX_ENTRY_BODY_CHARS)
            return body;

        // Never half an emoji: a cut between a surrogate pair leaves a lone high surrogate, which is not text.
        var length = char.IsHighSurrogate(body[MAX_ENTRY_BODY_CHARS - 1]) ? MAX_ENTRY_BODY_CHARS - 1 : MAX_ENTRY_BODY_CHARS;

        return $"{body[..length]}{CUT_MARK}";
    }
}
