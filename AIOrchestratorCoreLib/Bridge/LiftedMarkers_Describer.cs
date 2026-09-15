namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// WHAT THE MIRROR LIFTED OUT OF AN ENTRY BEFORE THE OWNER SAW IT.
///
/// <para>
/// The mirror removes marker lines from a body on the way to the phone, because the app turns them
/// into buttons and fields rather than sentences. That is right for a question and wrong for a
/// sentence that merely OPENS with a marker word — the match is anchored at column 0 and runs on
/// every entry, not only on questions, so a supervisor writing "DEFAULT: 30 giorni" or "ROW: the one
/// you asked about" loses that line between the channel file and the phone.
/// </para>
/// <para>
/// WHY A RECEIPT AND NOT A REFUSAL. The extraction is correct far more often than not, and guessing
/// intent from the text would be a second parser of the same words competing with the first. What was
/// missing is the receipt: the file said one thing, the phone showed another, and no surface could be
/// asked which. CLAUDE.md decision 21 is explicit that a component which cannot settle a question
/// says so rather than staying silent.
/// </para>
/// <para>
/// IT COMPARES, IT DOES NOT RE-DERIVE — and that is the whole of its correctness. The first version
/// asked <c>ChannelGrammar.Is_MarkerLine</c> whether each body line looked like a marker, and was
/// WRONG the first time it was run against the real extractor: that predicate trims before matching
/// and the extractor anchors at column 0, so an indented <c>  DEFAULT: …</c> survives the mirror and
/// would have been reported as lost. A receipt that invents losses is the false alarm that teaches
/// people to ignore the log — the same failure <c>ChannelShape_Validator</c> records for its own
/// first version. Diffing the body against what actually survived cannot drift from the extractor,
/// because it is not a second opinion about anything.
/// </para>
/// </summary>
public static class LiftedMarkers_Describer
{
    /// <summary>How much of a lifted line to quote — enough to recognise, short enough for a log.</summary>
    const int EXCERPT_LENGTH = 60;

    /// <summary>
    /// The line to log, or null when the entry lost nothing — the common case, and the reason this
    /// returns null rather than an empty string: nothing should be written for an ordinary entry.
    /// </summary>
    /// <param name="body">The entry's body, as the parser read it.</param>
    /// <param name="remaining">What is left of it after every marker extraction, on its way to the phone.</param>
    public static string? Describe_OrNull(string body, string remaining)
    {
        if (string.IsNullOrEmpty(body))
            return null;

        // Split on '\n' alone, exactly as the extractor does, so a CRLF body's lines are the same
        // strings on both sides and compare equal.
        var kept = new Queue<string>(remaining.Split('\n'));

        List<string> lifted = [];

        foreach (var line in body.Split('\n'))
        {
            if (kept.Count > 0 && kept.Peek() == line)
            {
                kept.Dequeue();
                continue;
            }

            // Blank lines go missing to the extractor's outer Trim('\n'), not to a marker match.
            if (line.Trim().Length == 0)
                continue;

            lifted.Add(Excerpt(line.Trim()));
        }

        if (lifted.Count == 0)
            return null;

        return $"{lifted.Count} marker line(s) lifted out of the body before sending: {string.Join(" · ", lifted)}";
    }

    static string Excerpt(string line)
    {
        return line.Length <= EXCERPT_LENGTH ? line : line[..EXCERPT_LENGTH].TrimEnd() + "…";
    }
}
