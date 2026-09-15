using System.Text.RegularExpressions;

namespace AIOrchestratorCoreLib.Channels;

/// <summary>
/// WHICH LINES OF A CHANNEL ARE QUOTED CODE — the one place this system decides that a
/// header-shaped line is EVIDENCE rather than a new entry.
///
/// <para>
/// WHY IT EXISTS. Observed 2026-09-15, reproduced from the owner's Telegram export: a supervisor
/// quoted an implementer's entry inside a ``` block, and <see cref="ChannelEntry_Parser"/> — which
/// had no fence state at all — split one message into two. The owner received the first half signed
/// "Sup" and the second half signed "Imp", in a separate message minutes later, opening mid-sentence
/// on "Quindi 273, 281, 288 e 043 sono lì da vedere." It could also delete text outright: when the
/// quoted author word is <c>owner</c> the tail is never mirrored at all, and when it is <c>app</c>
/// only the quoted subject survives.
/// </para>
/// <para>
/// IT IS ITS OWN COMPONENT BECAUSE THREE READERS NEED THE SAME ANSWER — the parser, the tailer
/// (which cuts appends at header lines) and <see cref="ChannelShape_Validator"/> (which must not
/// report a quoted malformed header as an invisible entry). A rule this codebase has spelled twice
/// has drifted every single time; the header pattern itself is already kept in one place for exactly
/// that reason.
/// </para>
/// <para>
/// ONLY A CLOSED FENCE SUPPRESSES ANYTHING, and that is the whole design. The obvious implementation
/// — "suppress while a fence is open" — turns one agent's stray ``` into a channel that stops
/// mirroring: every later entry disappears into one unbounded body, with nothing to notice it. That
/// is strictly worse than the defect being fixed. An opening delimiter with no partner therefore
/// suppresses NOTHING, which is precisely the behaviour that shipped before this component existed,
/// so the worst case of this rule is the behaviour it replaces.
/// </para>
/// </summary>
public static partial class ChannelFence_Screen
{
    /// <summary>
    /// A fenced-code delimiter: up to three leading spaces, then a run of at least three backticks or
    /// tildes, then an optional info string (<c>```json</c>).
    /// </summary>
    [GeneratedRegex(@"^ {0,3}(?<fence>`{3,}|~{3,})(?<info>.*)$", RegexOptions.Compiled)]
    private static partial Regex Fence_Regex();

    /// <summary>
    /// Whether this line is a fence delimiter AT ALL — the cheap half, for a caller that only needs
    /// to know whether fences are in play.
    ///
    /// <para>
    /// A file with no delimiter anywhere cannot contain a quoted header, so a scan that sees none can
    /// skip <see cref="Map_QuotedLines"/> and its allocations entirely. That is what keeps
    /// <c>ChannelEntry_Parser.Count_Entries</c> — asked of every channel on every two-second tick —
    /// from splitting the whole file into strings just to find out that nothing was quoted.
    /// </para>
    /// <para>
    /// IT IS NOT A SECOND COPY OF THE RULE: pairing, and therefore the decision, stays in
    /// <see cref="Map_QuotedLines"/>. This answers only "is there a delimiter on this line", which is
    /// the same question the map's first step asks, through the same regex.
    /// </para>
    /// </summary>
    public static bool Looks_LikeDelimiter(ReadOnlySpan<char> line)
    {
        return Fence_Regex().IsMatch(line);
    }

    /// <summary>
    /// One flag per line: true when that line lies inside a CLOSED fenced block, delimiters included.
    ///
    /// <para>
    /// Closing follows CommonMark where it is cheap: same delimiter character, a run at least as long
    /// as the opening one, and no info string. That keeps <c>```json</c> from closing a block opened
    /// with a plain <c>```</c>, which is the shape an agent pasting a payload actually writes.
    /// </para>
    /// </summary>
    public static bool[] Map_QuotedLines(IReadOnlyList<string> lines)
    {
        var quoted = new bool[lines.Count];

        var openLine = -1;
        var openChar = '\0';
        var openLength = 0;

        for (var i = 0; i < lines.Count; i++)
        {
            var match = Fence_Regex().Match(lines[i].TrimEnd('\r'));

            if (!match.Success)
                continue;

            var run = match.Groups["fence"].Value;
            var info = match.Groups["info"].Value.Trim();

            if (openLine < 0)
            {
                openLine = i;
                openChar = run[0];
                openLength = run.Length;
                continue;
            }

            if (run[0] != openChar || run.Length < openLength || info.Length > 0)
                continue;

            for (var inside = openLine; inside <= i; inside++)
                quoted[inside] = true;

            openLine = -1;
        }

        // An unclosed opening delimiter suppresses nothing — see the class summary.
        return quoted;
    }
}
