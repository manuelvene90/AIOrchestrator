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
    [GeneratedRegex(@"^##\s*\[(\d+)\]\s*FROM\s+(\S+)\s*(.*)$", RegexOptions.Compiled)]
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

        var body = string.Join('\n', entryLines.Skip(1)).Trim('\n');
        var rawText = string.Join('\n', entryLines).Trim('\n');

        return ChannelEntry_Factory.Create(index, author, dateText, subject, body, rawText);
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
