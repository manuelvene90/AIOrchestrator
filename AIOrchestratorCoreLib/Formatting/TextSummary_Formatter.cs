namespace AIOrchestratorCoreLib.Formatting;

/// <summary>
/// Trims agent-written text down to a glanceable length for the UI. Agents write full-sentence
/// entry subjects; a card row has to be readable at a glance, so it gets the first few words and
/// an ellipsis — the full text lives one click away in the detail window's activity feed.
/// </summary>
public static class TextSummary_Formatter
{
    /// <summary>Words a card row shows of a task subject before it stops being glanceable.</summary>
    public const int CARD_TASK_WORDS = 10;

    public static string Take_Words(string text, int maxWords)
    {
        if (string.IsNullOrWhiteSpace(text) || maxWords <= 0)
            return string.Empty;

        var words = text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (words.Length <= maxWords)
            return string.Join(' ', words);

        return $"{string.Join(' ', words.Take(maxWords))}…";
    }
}
