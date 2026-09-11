namespace AIOrchestratorCoreLib.Bridge.Decisions;

/// <summary>
/// Whether a decision on the owner's phone is one a single tap must not be allowed to take.
///
/// <para>
/// IT READS WHAT THE OWNER IS DECIDING — the question and its option labels, composed by the
/// caller. What makes "yes" dangerous is what it is an answer to, and the danger is as often in a
/// label ("Push the release branch to main") as in the question. Every option of a high-risk
/// question inherits the classification, including the refusals: that costs a typed code to say
/// NO, which is the safe direction to be wrong in, the alternative being to guess which label is
/// the dangerous one.
/// </para>
/// <para>
/// AND NOT THE NARRATIVE THE QUESTION CAME FROM. The entry body was in that surface until
/// 2026-09-07 and it locked four pure product questions in one afternoon, because the prose around
/// them said "the deployed engine crashes on these keys" and "a deploy check already blocks this".
/// Nothing was being deployed. A lock that fires on what the agent happened to mention is a lock
/// the owner learns to type through.
/// </para>
/// <para>
/// WHOLE WORDS, CASE-INSENSITIVE, AND STILL NOT A REGEX THE OWNER WRITES. The list is edited in
/// config.json by a person under pressure, and a regex there is a way to write a pattern that
/// matches everything or throws at parse time — neither of which announces itself. So a pattern is
/// still a plain string; it simply has to sit on a word boundary. `push` fires on "push it now" and
/// not on "I pushed it already"; `rm -rf`, `--force` and `reset --hard` match exactly as written,
/// because a boundary is only required where the pattern's own edge is a letter or a digit.
/// </para>
/// </summary>
public static class HighRisk_Classifier
{
    /// <summary>
    /// True when any configured pattern appears in the question. An empty pattern list means the
    /// owner has said nothing is high risk, and nothing is: this returns false rather than falling
    /// back to a default, because the fallback belongs to the config factory and applying it twice
    /// would make an explicit empty list impossible to express.
    /// </summary>
    public static bool Is_HighRisk(string? questionText, IReadOnlyList<string> patterns)
    {
        if (string.IsNullOrWhiteSpace(questionText))
            return false;

        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;

            if (Matches(questionText, pattern))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Which pattern matched, for the log line that explains why the owner is being asked for a
    /// code. "It looked risky" is not actionable; "it matched 'deploy'" is, and it is what lets
    /// them fix a pattern that fires too often.
    /// </summary>
    public static string? Find_MatchedPattern_OrNull(string? questionText, IReadOnlyList<string> patterns)
    {
        if (string.IsNullOrWhiteSpace(questionText))
            return null;

        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;

            if (Matches(questionText, pattern))
                return pattern;
        }

        return null;
    }

    /// <summary>
    /// The pattern, on a word boundary, case-insensitively — the single reading both answers above
    /// are taken from, so the decision and the log line that explains it can never disagree.
    ///
    /// <para>
    /// The boundary is asserted only against letters and digits, which is what makes a pattern like
    /// <c>--force</c> or <c>rm -rf</c> work unchanged: its own first and last characters are
    /// punctuation, so nothing adjacent to them is "the same word". The pattern is escaped before it
    /// reaches the engine, so a config entry is still a literal string and never a regex.
    /// </para>
    /// </summary>
    static bool Matches(string surface, string pattern)
    {
        var escaped = System.Text.RegularExpressions.Regex.Escape(pattern.Trim());

        return System.Text.RegularExpressions.Regex.IsMatch(
            surface,
            $@"(?<![\p{{L}}\p{{N}}]){escaped}(?![\p{{L}}\p{{N}}])",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }
}
