using System.Text.RegularExpressions;

namespace AIOrchestratorCoreLib.Formatting;

/// <summary>
/// Condenses an agent-written subject into a glanceable task label. Agents write full sentences
/// ("Task 3: fix the screener so greedy and cluster discovery actually start after the download
/// phase"); a card row needs the gist ("fix screener"). Truncating mid-sentence with an ellipsis
/// was worse than useless — it cut away the object of the verb — so this SUMMARISES instead:
/// drop the protocol prefix, keep the leading clause (which is the task; everything after "so",
/// "because", a comma or a dash is justification or detail), then drop filler words if it is still
/// long. Deterministic: no model call on a UI refresh path.
/// </summary>
public static partial class TextSummary_Formatter
{
    /// <summary>Words a card row shows of a task before it stops being glanceable.</summary>
    public const int CARD_TASK_WORDS = 10;

    /// <summary>
    /// Bookkeeping that precedes the actual task, stripped one layer at a time. Kept as separate
    /// narrow patterns on purpose: a single combined one swallowed "brief for imp-2 —" as far as
    /// the hyphen INSIDE "imp-2" and left a stray "2".
    /// </summary>
    [GeneratedRegex(@"^\s*\[[^\]]*\]\s*", RegexOptions.Compiled)]
    private static partial Regex BracketTag_Regex();

    [GeneratedRegex(@"^\s*(task|step|item|point)\s*\d*\s*[:.\)]\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex TaskNumber_Regex();

    [GeneratedRegex(@"^\s*\d+\s*[:.\)]\s*", RegexOptions.Compiled)]
    private static partial Regex LeadingNumber_Regex();

    [GeneratedRegex(@"^\s*(brief|verdict|report|review)(\s+(for|on|from)\s+[\w-]+)?\s*[:—–]\s*|^\s*(brief|verdict|report|review)(\s+(for|on|from)\s+[\w-]+)?\s+-\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex RoleLabel_Regex();

    /// <summary>Where the task ends and the reasoning begins.</summary>
    [GeneratedRegex(@"\s+(so that|so|because|since|in order to|which|while|after|before|when|then)\s+|[,;:]|\s+[—–-]\s+|\s*\(", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ClauseBreak_Regex();

    /// <summary>Words that carry no meaning at a glance; dropped only when the label is still too long.</summary>
    static readonly IReadOnlySet<string> FILLER_WORDS = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "of", "for", "to", "into", "on", "in", "at", "its", "their", "our",
        "actually", "properly", "correctly", "really", "just", "also", "all", "some", "any",
    };

    public static string Summarize_Task(string subject, int maxWords)
    {
        if (string.IsNullOrWhiteSpace(subject) || maxWords <= 0)
            return string.Empty;

        var stripped = Strip_Bookkeeping(subject.Trim());

        if (stripped.Length == 0)
            stripped = subject.Trim();

        var leadingClause = ClauseBreak_Regex().Split(stripped)[0].Trim();
        var words = Split_Words(leadingClause);

        // A one-word clause is fine when the word carries the meaning ("accepted"), useless when
        // it is a bare verb ("fix, because…") — in that case keep the whole subject and let the
        // filler filter shorten it instead.
        const int SHORTEST_MEANINGFUL_SINGLE_WORD = 5;

        if (words.Count == 0 || (words.Count == 1 && words[0].Trim(',', '.', ';').Length < SHORTEST_MEANINGFUL_SINGLE_WORD))
            words = Split_Words(stripped);

        if (words.Count > maxWords)
        {
            var withoutFiller = words.Where(word => !FILLER_WORDS.Contains(word.Trim(',', '.', ';'))).ToList();

            if (withoutFiller.Count >= 2)
                words = withoutFiller;
        }

        // Still long: a hard stop, without an ellipsis — the full text is in the activity feed.
        if (words.Count > maxWords)
            words = [.. words.Take(maxWords)];

        return string.Join(' ', words).TrimEnd('.', ',', ';', ':', '-', '—', '–');
    }

    /// <summary>Peels every bookkeeping layer, so "[imp-2] Task 3: brief — do X" reduces to "do X".</summary>
    static string Strip_Bookkeeping(string subject)
    {
        var text = subject;

        for (var pass = 0; pass < 4; pass++)
        {
            var before = text;

            text = BracketTag_Regex().Replace(text, string.Empty);
            text = TaskNumber_Regex().Replace(text, string.Empty);
            text = LeadingNumber_Regex().Replace(text, string.Empty);
            text = RoleLabel_Regex().Replace(text, string.Empty);

            if (text == before)
                break;
        }

        return text.Trim();
    }

    static List<string> Split_Words(string text)
    {
        return [.. text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)];
    }
}
