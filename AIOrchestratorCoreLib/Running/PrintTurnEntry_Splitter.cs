using AIOrchestratorCoreLib.Channels;

namespace AIOrchestratorCoreLib.Running;

/// <summary>
/// A session's final message into a channel entry's (subject, body). The contract the role
/// command's print paragraph states: first line = subject, blank line, body. A message that does
/// not follow it still becomes an entry — its first line, trimmed of markdown heading marks and
/// capped, is the subject and the whole text is the body — so nothing a session says is dropped
/// for want of a format.
/// </summary>
public static class PrintTurnEntry_Splitter
{
    public const int MAX_SUBJECT_LENGTH = 120;
    public const string EMPTY_SUBJECT = "(no message)";

    /// <summary>
    /// What a header-shaped line in the body is turned into. A markdown quote marker: the line stays
    /// readable and stays in the entry, but it no longer BEGINS with the header pattern, which is
    /// the only thing the parser looks at.
    /// </summary>
    public const string NEUTRALISED_HEADER_PREFIX = "> ";

    public static (string Subject, string Body) Split(string? resultText)
    {
        var text = Strip_LeadingNarration((resultText ?? string.Empty).Replace("\r\n", "\n").Trim());

        if (text.Length == 0)
            return (EMPTY_SUBJECT, string.Empty);

        var lines = text.Split('\n');
        var firstLine = Clean_SubjectLine(lines[0]);

        if (lines.Length >= 3 && lines[1].Trim().Length == 0 && firstLine.Length > 0 && firstLine.Length <= MAX_SUBJECT_LENGTH)
            return (firstLine, Neutralise_HeaderLines(string.Join('\n', lines.Skip(2)).Trim()));

        var subject = firstLine.Length == 0 ? EMPTY_SUBJECT : firstLine;

        if (subject.Length > MAX_SUBJECT_LENGTH)
            subject = subject[..(MAX_SUBJECT_LENGTH - 1)].TrimEnd() + "…";

        return (subject, Neutralise_HeaderLines(text));
    }

    /// <summary>
    /// THE SESSION NARRATING THAT IT IS ABOUT TO WRITE THE ENTRY IS NOT THE ENTRY.
    ///
    /// <para>
    /// Measured live on 2026-09-07, general channel entry #52: the final message began
    /// <c>Now writing my final message (which is the channel entry in print-runner mode):</c>, a
    /// blank line, and then the message. That first line became the SUBJECT — it is short, it is
    /// followed by a blank line, so it satisfies the contract exactly — and every reader of that
    /// channel, the owner included, got a sentence about the mechanism instead of the news.
    /// </para>
    /// <para>
    /// It is ordinary model behaviour rather than a slip: the print prompt tells the session that
    /// its reply becomes an entry, and announcing what one is about to do is what a session does
    /// with an instruction like that. So it is handled here rather than by another sentence in the
    /// role protocol.
    /// </para>
    /// <para>
    /// TWO SHAPES ARE STRIPPED AND NEITHER GUESSES. The first is STRUCTURAL and needs no vocabulary:
    /// a leading line ending in a colon whose next non-blank line the parser reads as a channel
    /// HEADER cannot be the subject, because the session has plainly written the header itself and
    /// what precedes it is preamble. The second is the narration above, recognised by an opener that
    /// speaks about WRITING THE MESSAGE — kept to phrases actually observed, because a broad "any
    /// line ending in a colon" would eat a real subject like <c>BLOCKED ON OWNER:</c>.
    /// </para>
    /// <para>
    /// Only leading lines, and only while they keep matching: a colon line in the middle of a body
    /// is prose and is left exactly where the session put it.
    /// </para>
    /// </summary>
    static string Strip_LeadingNarration(string text)
    {
        var remaining = text;

        while (true)
        {
            var lines = remaining.Split('\n');
            var first = lines[0].Trim();

            if (!first.EndsWith(':') || ChannelEntry_Parser.Is_HeaderLine(first))
                return remaining;

            var rest = string.Join('\n', lines.Skip(1)).Trim();

            // Dropping it would leave nothing: then the narration IS the whole message, and a
            // sentence about the mechanism still beats "(no message)".
            if (rest.Length == 0)
                return remaining;

            if (!Is_HeaderNext(rest) && !Is_WritingNarration(first))
                return remaining;

            remaining = rest;
        }
    }

    /// <summary>The next thing the session wrote is a header of its own, so what came before it was preamble.</summary>
    static bool Is_HeaderNext(string rest)
    {
        foreach (var line in rest.Split('\n'))
        {
            if (line.Trim().Length == 0)
                continue;

            return ChannelEntry_Parser.Is_HeaderLine(line.Trim());
        }

        return false;
    }

    /// <summary>
    /// Openers observed in live final messages. A LIST, deliberately short, and it earns its keep by
    /// being anchored at the START of the line: "Now writing my final message …:" is narration,
    /// while a subject that happens to contain the word "writing" is not.
    /// </summary>
    static readonly IReadOnlyList<string> WRITING_NARRATION_OPENERS =
    [
        "now writing",
        "now filing",
        "writing my",
        "writing the",
        "filing my",
        "filing the",
        "here is my",
        "here is the",
        "here's my",
        "here's the",
        "let me write",
        "i'll write",
        "i will write",
        "my final message",
        "final message",
        "channel entry",
    ];

    static bool Is_WritingNarration(string line)
    {
        var lowered = line.ToLowerInvariant();

        foreach (var opener in WRITING_NARRATION_OPENERS)
        {
            if (lowered.StartsWith(opener, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// A BODY LINE MAY NOT LOOK LIKE A HEADER, and this is the boundary where untrusted text becomes
    /// an entry, so it is where the rule belongs.
    ///
    /// <para>
    /// The prompt hands the session its pending entries VERBATIM, header lines included, and then
    /// tells it that its reply becomes an entry — quoting the context back is ordinary model
    /// behaviour, not an attack. An echoed <c>## [12] FROM supervisor — …</c> parses as a real
    /// entry: with an index above the cursor and an inbound author it triggers another turn for the
    /// same session, whose answer can echo it again; and because <c>ChannelEntry_Parser.Get_NextIndex</c>
    /// takes the MAXIMUM, a made-up index also jumps the numbering for every later writer.
    /// </para>
    /// <para>
    /// Neutralised rather than rejected: the turn's work is real and its words are the record. The
    /// line survives, quoted, and says plainly that it was quoted.
    /// </para>
    /// </summary>
    static string Neutralise_HeaderLines(string body)
    {
        if (body.Length == 0)
            return body;

        var lines = body.Split('\n');
        var neutralised = false;

        for (var i = 0; i < lines.Length; i++)
        {
            if (!ChannelEntry_Parser.Is_HeaderLine(lines[i]))
                continue;

            lines[i] = NEUTRALISED_HEADER_PREFIX + lines[i];
            neutralised = true;
        }

        return neutralised ? string.Join('\n', lines) : body;
    }

    /// <summary>
    /// The first line, made safe to sit inside a header the bridge is about to write around it.
    ///
    /// <para>
    /// A heading mark would turn the subject into a second header — the parser matches '## [' at line
    /// start — so it goes, together with bold marks.
    /// </para>
    /// <para>
    /// AND A SESSION THAT ECHOES ITS OWN HEADER NAMES ITS SUBJECT TWICE. Stripping the '##' off
    /// <c>## [2] FROM implementer — 2026-09-06 — Brief complete</c> leaves the whole header as the
    /// subject, and the bridge then wraps a real one around it:
    /// <c>## [2] FROM implementer — 2026-09-06 12:30 — [2] FROM implementer — 2026-09-06 — Brief
    /// complete</c>. Cosmetic — the body is neutralised separately and a subject cannot open a phantom
    /// entry — but it carries a SECOND index and a SECOND date, both invented by the session, inside
    /// the ones the bridge just wrote. Measured live twice on 2026-09-06, in the stage-1b round and
    /// again in the stage-1c one, so it is what a model ordinarily does rather than a rare slip.
    /// </para>
    /// <para>
    /// The echoed subject is recovered THROUGH THE PARSER rather than by a pattern of this file's own:
    /// a second copy of the header rule is how a legend, two ledger parsers and a marker list drifted
    /// apart in one evening. That also fixes the boundary of the fix: a line the parser does not read
    /// as a header — no <c>##</c>, or only one em-dash, which it reads as a date with no subject —
    /// falls through to the stripping below and keeps the behaviour it had. Widening it would mean
    /// inventing a laxer header shape here, which is the drift itself.
    /// </para>
    /// </summary>
    static string Clean_SubjectLine(string line)
    {
        var trimmed = line.Trim();

        if (ChannelEntry_Parser.Is_HeaderLine(trimmed))
        {
            var echoed = ChannelEntry_Parser.Parse_All(trimmed);

            if (echoed.Count == 1 && echoed[0].Subject.Length > 0)
                return Strip_Marks(echoed[0].Subject);
        }

        return Strip_Marks(trimmed);
    }

    static string Strip_Marks(string line)
    {
        return line.Trim().TrimStart('#').Trim().Trim('*').Trim();
    }
}
