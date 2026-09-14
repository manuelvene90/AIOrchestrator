namespace AIOrchestratorCoreLib.Bridge.SuppressedEntries;

/// <summary>
/// THE DRAINED ENTRIES AS ONE MESSAGE — the "last words" half of the turn-ended completion.
///
/// <para>
/// ONE MESSAGE, IN ORDER, NOT THE LAST LINE. A session answers and then writes its "WAITING ON …"
/// status line; handing the owner only the last thing held gave them the status line and lost the
/// answer (2026-09-10, three times in one morning). Each text already carries its speaker glyph, so
/// nothing is added between them but a blank line.
/// </para>
/// </summary>
public static class SuppressedDigest_Builder
{
    /// <summary>
    /// The non-blank texts joined by a blank line, or null when there is nothing to read — a blank
    /// paragraph is not a message, and the caller sends nothing on null.
    /// </summary>
    public static string? Build_OrNull(IReadOnlyList<(string? Subject, string Text)> entries)
    {
        var texts = entries
            .Select(entry => entry.Text.Trim('\n', '\r'))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        return texts.Count == 0 ? null : string.Join("\n\n", texts);
    }
}
