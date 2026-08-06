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

        return entries[entries.Count - 1].Index + 1;
    }

    public static bool Is_HeaderLine(string line)
    {
        return Header_Regex().IsMatch(line.TrimEnd('\r'));
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

    static ChannelAuthors Parse_Author(string authorWord)
    {
        var normalized = authorWord.Trim().ToLowerInvariant();

        return normalized switch
        {
            "supervisor" => ChannelAuthors.Supervisor,
            "implementer" => ChannelAuthors.Implementer,
            "owner" => ChannelAuthors.Owner,
            "app" => ChannelAuthors.App,
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
