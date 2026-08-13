namespace AIOrchestratorCoreLib.Channels;

/// <summary>One header line, and where it lives — the live file or the sibling archive.</summary>
public readonly record struct ChannelHeaderLine(string Source, int LineNumber, int Index, string Line);

/// <summary>
/// Two header lines whose indices go BACKWARDS, named by FILE ORDER and nothing else.
///
/// WHICH OF THE TWO IS THE INTRUDER DEPENDS ON WHAT WAS QUOTED, and the screen cannot tell:
///
///   `[107]` … `[106]` … `[108]`  — quoting an OLDER header: the intruder is <see cref="Later"/>
///   `[94]`  … `[200]` … `[95]`   — quoting a NEWER or foreign one: the intruder is <see cref="Earlier"/>
///
/// The first shape is what happened on this machine. The second is the one that was tested when the
/// rule "the intruder is the earlier line" was written down — which is how a claim true of one case
/// came to be stated about both.
/// </summary>
public readonly record struct ChannelIndexCrossing(ChannelHeaderLine Earlier, ChannelHeaderLine Later);

/// <summary>
/// A SCREEN for header lines that parse perfectly and should not be entries at all — the commonest
/// being a header QUOTED inside another entry's body.
///
/// IT IS THE OTHER HALF OF <see cref="ChannelShape_Validator"/>, which finds lines that look like
/// headers and do NOT parse. A quoted header is the exact inverse: it parses, so that validator skips
/// it by design, and the app then reads someone's quotation as a real entry — mis-attributing a body
/// to whoever was quoted and consuming an index that a later entry will collide with.
///
/// WHY IT IS A SCREEN AND NOT A CHECK, and this must survive into the output: a backwards index is
/// ALSO what a legitimate crossing looks like, where two authors allocate the same number in the same
/// minute because each read the file before the other appended. On the channel this was written for,
/// one hit is a genuine crossing at 14:44/15:07 and one is a real quoted header — **and nothing
/// mechanical can separate them.** It hands a human a short list to read, where today there is none.
///
/// IT NAMES A PAIR, NEVER A CULPRIT. Consecutive indices are compared, so a crossing surfaces only
/// once a second header disagrees with the first — and which of the two is the intruder depends on
/// whether the quoted index was lower or higher than its surroundings. Both are printed with their
/// line numbers; a screen that misstates which line it means costs more than it saves.
///
/// It cannot see a quoted header that is still the LAST header in the file: nothing has followed it to
/// fall back. That window closes as soon as anybody appends.
/// </summary>
public static class ChannelIndexSequence_Screen
{
    public const string LIVE_SOURCE = "live";
    public const string ARCHIVE_SOURCE = "archive";

    /// <summary>
    /// The whole history's header lines, archive first — decision 13. A live-file-only pass reads a
    /// COMPACTED channel as a file that starts at index 84, and every archive boundary as a hole.
    /// </summary>
    public static IReadOnlyList<ChannelHeaderLine> Read_Headers(string archiveText, string liveText)
    {
        List<ChannelHeaderLine> headers = [];

        foreach (var (lineNumber, index, line) in ChannelEntry_Parser.Read_HeaderLines(archiveText))
            headers.Add(new ChannelHeaderLine(ARCHIVE_SOURCE, lineNumber, index, line));

        foreach (var (lineNumber, index, line) in ChannelEntry_Parser.Read_HeaderLines(liveText))
            headers.Add(new ChannelHeaderLine(LIVE_SOURCE, lineNumber, index, line));

        return headers;
    }

    /// <summary>
    /// Every place the index sequence goes backwards. Consecutive comparison only: it catches a
    /// repeated index and an out-of-range one in either direction, because a too-HIGH intruder is
    /// caught by the next real entry falling back below it.
    /// </summary>
    public static IReadOnlyList<ChannelIndexCrossing> Find_Crossings(IReadOnlyList<ChannelHeaderLine> headers)
    {
        List<ChannelIndexCrossing> crossings = [];

        for (var i = 1; i < headers.Count; i++)
        {
            if (headers[i].Index < headers[i - 1].Index)
                crossings.Add(new ChannelIndexCrossing(headers[i - 1], headers[i]));
        }

        return crossings;
    }

    /// <summary>
    /// One log line per crossing, printing BOTH lines with their line numbers and naming NEITHER as
    /// the culprit — because the screen genuinely cannot tell.
    ///
    /// An earlier version labelled one of them SUSPECT. That was true of the case it had been tested
    /// against and false of the case that actually occurred here, so it would have pointed a reader at
    /// the innocent entry roughly half the time — worse than pointing at neither, because it reads as
    /// knowledge.
    /// </summary>
    public static string Describe_Crossing(ChannelIndexCrossing crossing)
    {
        return
            $"index runs backwards: [{crossing.Earlier.Index}] then [{crossing.Later.Index}]. "
            + $"{crossing.Earlier.Source} line {crossing.Earlier.LineNumber}: {crossing.Earlier.Line} — "
            + $"{crossing.Later.Source} line {crossing.Later.LineNumber}: {crossing.Later.Line}. "
            + "SCREEN, not a verdict: ONE of these two may be a header quoted inside another entry's body — which one depends on what was quoted — "
            + "and the pair is equally what two authors allocating one index in the same minute looks like. Read both lines.";
    }

    /// <summary>
    /// The key a caller de-dups on, so a crossing is reported once rather than on every sweep.
    ///
    /// It names BOTH lines, because either may be the intruder and neither alone identifies the pair.
    /// A channel carrying an old legitimate crossing would otherwise re-log for as long as the app
    /// runs — the waterfall this system exists to prevent, in the log rather than on a phone.
    /// </summary>
    public static string Build_DedupeKey(ChannelIndexCrossing crossing)
    {
        return
            $"{crossing.Earlier.Source}|{crossing.Earlier.LineNumber}|{crossing.Earlier.Line}"
            + $"||{crossing.Later.Source}|{crossing.Later.LineNumber}|{crossing.Later.Line}";
    }
}
