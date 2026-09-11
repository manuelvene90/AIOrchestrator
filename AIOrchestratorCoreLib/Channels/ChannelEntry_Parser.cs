using System.Text.RegularExpressions;
using AIOrchestratorCoreLib.Channels.ChannelEntry;

namespace AIOrchestratorCoreLib.Channels;

/// <summary>
/// Parses append-only channel text into entries. Header format:
/// '## [n] FROM &lt;author&gt; — &lt;date&gt; — &lt;subject&gt;'.
/// The em-dash also appears inside subjects, so the header is split on the FIRST two em-dashes only.
/// Text before the first header (file seed/preamble) is ignored.
/// </summary>
public static partial class ChannelEntry_Parser
{
    /// <summary>
    /// THE HEADER SHAPE, named so it can be COMPARED with the grammar the tool writes from.
    ///
    /// <para>
    /// It cannot be read from <see cref="ChannelGrammar"/> at runtime: <c>[GeneratedRegex]</c> needs a
    /// compile-time constant, and the source-generated matcher is why this parse is cheap enough to
    /// run on every tick. So the pattern stays a literal here — and
    /// <c>ChannelGrammarTests.TheGrammarsHeaderPatternIsTheOneTheParserCompiles</c> asserts it equals
    /// the grammar's, which is the cheapest thing that cannot silently rot. Two spellings of a header
    /// is the drift that produced four header variants on 2026-08-08.
    /// </para>
    /// </summary>
    public const string HEADER_PATTERN = @"^##\s*\[(\d+)\]\s*FROM\s+(\S+)\s*(.*)$";

    [GeneratedRegex(HEADER_PATTERN, RegexOptions.Compiled)]
    private static partial Regex Header_Regex();

    const string EM_DASH = "—";

    public static IReadOnlyList<IChannelEntry> Parse_All(string channelText)
    {
        List<IChannelEntry> entries = [];

        if (string.IsNullOrEmpty(channelText))
            return entries;

        var lines = channelText.Split('\n');
        List<string> currentLines = [];
        Match? currentHeader = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            var headerMatch = Header_Regex().Match(line);

            if (headerMatch.Success)
            {
                if (currentHeader != null)
                    entries.Add(Build_Entry(currentHeader, currentLines));

                currentHeader = headerMatch;
                currentLines = [line];
            }
            else if (currentHeader != null)
            {
                currentLines.Add(line);
            }
        }

        if (currentHeader != null)
            entries.Add(Build_Entry(currentHeader, currentLines));

        return entries;
    }

    /// <summary>
    /// HOW MANY ENTRIES the text holds, WITHOUT BUILDING ANY OF THEM. Same splitting and same header
    /// pattern as <see cref="Parse_All"/> — it is that method with the entry construction removed —
    /// so the two can never answer differently about the same text.
    ///
    /// <para>
    /// IT EXISTS FOR ONE CALLER AND ONE QUESTION: <see cref="Channel_Compactor"/> asks, of every
    /// channel, on every 2-second tick, whether the file is over its threshold — and the answer is
    /// "no" for every short-lived orchestration in the system. Paying <see cref="Parse_All"/> for that
    /// no meant allocating an entry object, a line list and four substrings per entry, for every
    /// channel, thirty times a minute, to throw all of it away.
    /// </para>
    /// <para>
    /// IT IS A PRE-FILTER, NOT THE DECISION. The compactor still parses before it archives anything:
    /// what leaves the live file has to be the entries themselves, and a count is not evidence about
    /// them.
    /// </para>
    /// </summary>
    public static int Count_Entries(string channelText)
    {
        if (string.IsNullOrEmpty(channelText))
            return 0;

        var count = 0;
        var remaining = channelText.AsSpan();

        while (!remaining.IsEmpty)
        {
            var lineBreak = remaining.IndexOf('\n');
            var line = lineBreak < 0 ? remaining : remaining[..lineBreak];

            if (Header_Regex().IsMatch(line.TrimEnd('\r')))
                count++;

            if (lineBreak < 0)
                break;

            remaining = remaining[(lineBreak + 1)..];
        }

        return count;
    }

    public static int Get_NextIndex(string channelText)
    {
        var entries = Parse_All(channelText);

        if (entries.Count == 0)
            return 1;

        // The HIGHEST index used, not the last one written. Entries are appended by several
        // writers and a file that has already suffered a collision is not sorted — numbering from
        // the tail then hands out an index that exists further up, turning one duplicate into a
        // run of them.
        return entries.Max(entry => entry.Index) + 1;
    }

    public static bool Is_HeaderLine(string line)
    {
        return Header_Regex().IsMatch(line.TrimEnd('\r'));
    }

    /// <summary>
    /// Every header line with WHERE it is and what index it claims — for callers that need to reason
    /// about the header lines themselves rather than about the entries they open.
    ///
    /// It exists so that <see cref="ChannelIndexSequence_Screen"/> does not carry a second copy of
    /// <c>Header_Regex</c>. A duplicated header pattern is the drift that has cost this codebase a
    /// legend, two ledger parsers and a marker list in one evening — and it would be a particularly
    /// poor place to start, since the screen's whole job is to notice header lines that should not be
    /// there.
    ///
    /// Line numbers are 1-based, matching <see cref="ChannelShape_Validator.Find_MalformedHeaders"/>,
    /// so the two report the same coordinates for the same file.
    /// </summary>
    public static IReadOnlyList<(int LineNumber, int Index, string Line)> Read_HeaderLines(string channelText)
    {
        List<(int LineNumber, int Index, string Line)> headers = [];

        if (string.IsNullOrEmpty(channelText))
            return headers;

        var lines = channelText.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var match = Header_Regex().Match(line);

            if (match.Success)
                headers.Add((i + 1, int.Parse(match.Groups[1].Value), line.Trim()));
        }

        return headers;
    }

    static IChannelEntry Build_Entry(Match header, IReadOnlyList<string> entryLines)
    {
        var index = int.Parse(header.Groups[1].Value);
        var author = Parse_Author(header.Groups[2].Value);
        var afterAuthor = header.Groups[3].Value;

        var (dateText, subject) = Split_DateAndSubject(afterAuthor);

        var bodyLines = entryLines.Skip(1).ToList();

        // THE DECLARED TYPE COMES OUT OF THE BODY (E3 requirement 3). The tool writes it directly
        // under the header, and it is METADATA — so it must not travel on as body text: the mirror
        // would put "type: question" on the owner's phone, which is the app's bookkeeping read aloud.
        // Same reasoning as the STATE: line, which brief C strips for the same reason.
        //
        // ONLY THE FIRST NON-BLANK LINE IS CONSIDERED. A prose line beginning "type: " further down
        // is prose — the owner writes about types — and the tool always emits this one first.
        string? declaredType = null;
        var firstContentIndex = bodyLines.FindIndex(line => !string.IsNullOrWhiteSpace(line));

        if (firstContentIndex >= 0
            && bodyLines[firstContentIndex].StartsWith(ChannelGrammar.TypeLine_Prefix, StringComparison.Ordinal))
        {
            declaredType = bodyLines[firstContentIndex][ChannelGrammar.TypeLine_Prefix.Length..].Trim();
            bodyLines.RemoveAt(firstContentIndex);

            if (declaredType.Length == 0)
                declaredType = null;
        }

        var body = string.Join('\n', bodyLines).Trim('\n');

        // RawText KEEPS THE TYPE LINE, deliberately: it is the entry as written, and the audit trail
        // is the one place the app's own bookkeeping belongs. Every recogniser that reads RawText
        // matches a marker at a line start, and `type: ` is not one.
        var rawText = string.Join('\n', entryLines).Trim('\n');

        return ChannelEntry_Factory.Create(index, author, dateText, subject, body, rawText, declaredType);
    }

    /// <summary>
    /// The author word, with surrounding punctuation and markdown stripped before it is matched.
    ///
    /// The header regex captures a bare token, so `FROM **implementer**`, `FROM implementer:` and
    /// `FROM _supervisor_` all fall through to <see cref="ChannelAuthors.Unknown"/> without this.
    ///
    /// SPECULATIVE, AND SAYING SO IS THE POINT. An earlier version of this comment claimed the shape
    /// was observed live. It was not: 3,406 headers on this machine carry five distinct author words
    /// and ZERO decorated ones, and the incidents that were cited turned out to be header SHAPE
    /// defects — a missing index, two non-numeric ones — not author drift. The guard stays because it
    /// is near-harmless and the failure it prevents is severe, but it is a defence against a shape
    /// nobody has seen. A defence labelled speculative is fine; one labelled measured is how the next
    /// reader stops checking.
    ///
    /// The severity is what earns it. Window markers are read only from member-authored entries, so
    /// an entry that OPENS a window under a clean header and CLOSES it under a drifted one leaves the
    /// close invisible — and a missing close reads as still-open, forever, with the app then telling
    /// the member to append the close it already appended.
    ///
    /// Normalised HERE, where the author word is interpreted, rather than in the resolver that
    /// happened to notice: a second normaliser would be one more rule with two copies.
    /// </summary>
    static ChannelAuthors Parse_Author(string authorWord)
    {
        var normalized = authorWord.Trim().ToLowerInvariant().Trim('*', '_', '`', ':', ',', '.', ';', '(', ')', '[', ']', '"', '\'');

        return normalized switch
        {
            "supervisor" => ChannelAuthors.Supervisor,
            "implementer" => ChannelAuthors.Implementer,
            "reviewer" => ChannelAuthors.Reviewer,
            "solo" => ChannelAuthors.Solo,
            "owner" => ChannelAuthors.Owner,
            "app" => ChannelAuthors.App,
            "communicator" => ChannelAuthors.Communicator,
            _ => ChannelAuthors.Unknown,
        };
    }

    static (string DateText, string Subject) Split_DateAndSubject(string afterAuthor)
    {
        var firstDash = afterAuthor.IndexOf(EM_DASH, StringComparison.Ordinal);
        if (firstDash < 0)
            return (string.Empty, afterAuthor.Trim());

        var afterFirst = afterAuthor[(firstDash + EM_DASH.Length)..];
        var secondDash = afterFirst.IndexOf(EM_DASH, StringComparison.Ordinal);

        if (secondDash < 0)
            return (afterFirst.Trim(), string.Empty);

        var dateText = afterFirst[..secondDash].Trim();
        var subject = afterFirst[(secondDash + EM_DASH.Length)..].Trim();

        return (dateText, subject);
    }
}
