using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Status;

namespace AIOrchestratorCoreLib.Reviewing;

/// <summary>
/// "DOES THIS ONE LINE DECLARE THIS MARKER?" — asked of <see cref="MemberState_Resolver.Contains_Marker"/>,
/// the repo's ONE matcher, rather than answered again here.
///
/// <para>
/// WHY A PROBE ENTRY AND NOT A REGEX. Every rule that matcher carries was paid for by a live failure:
/// a marker inside a sentence is discussion and not a declaration (a brief WARNING a reviewer about
/// this bug pinned it for four hours); decoration is stripped by CATEGORY because the character list
/// is unguessable (it met "🚩 BLOCKED ON OWNER"); a leading <c>&gt;</c>, <c>"</c> or <c>'</c> is
/// quotation and declares nothing; and a marker followed by a letter or digit is a longer word. A
/// second spelling of that here would start out missing all of them (CLAUDE.md decision 12). The
/// line-level predicate is private, so this asks the public one with a single-line entry whose
/// SUBJECT is empty — the subject branch cannot match an empty string, so what answers is exactly
/// the body rule.
/// </para>
/// <para>
/// THE COST IS ONE SMALL OBJECT PER LINE, on entries that carry a <c>REROUTE:</c> or <c>FIXED:</c>
/// marker at all — a handful per round, never a per-tick loop over a channel.
/// </para>
/// </summary>
public static class MarkerLine_Screen
{
    public static bool Declares(string line, string marker)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var probe = ChannelEntry_Factory.Create(
            index: 1, author: ChannelAuthors.Unknown, dateText: string.Empty,
            subject: string.Empty, body: line, rawText: line);

        return MemberState_Resolver.Contains_Marker(probe, marker);
    }

    /// <summary>
    /// What the writer put AFTER the marker on a line that <see cref="Declares"/> has already
    /// accepted, trimmed. Null when the line declares nothing.
    ///
    /// <para>
    /// This is a SLICE, not a second match: the decision that the line declares the marker is made
    /// above, by the one matcher; all this does is cut the text after the word it found. A caller
    /// that sliced without asking first would be reading an argument out of a quotation.
    /// </para>
    /// </summary>
    public static string? Read_Argument_OrNull(string line, string marker)
    {
        if (!Declares(line, marker))
            return null;

        var at = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

        if (at < 0)
            return null;

        // MARKDOWN DECORATION IS TRIMMED OFF THE ARGUMENT for the same reason the matcher strips it
        // off the front of the line: `**REROUTE:** rev-1 from abc1234` is a declaration written by
        // someone using markdown, and the one matcher already accepts it. Only decoration is removed —
        // nothing here interprets, so an argument that still does not parse is refused, never guessed.
        return line[(at + marker.Length)..].Trim().Trim('*', '_', '`').Trim();
    }
}
