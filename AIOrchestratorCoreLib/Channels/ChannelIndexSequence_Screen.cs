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
    /// Every place the index sequence FAILS TO ADVANCE — backwards or flat. Consecutive comparison
    /// only: an out-of-range index is caught in either direction, because a too-HIGH intruder is
    /// caught by the next real entry falling back below it.
    ///
    /// NOT STRICT, and it was. `&lt;` cannot see `[80]`, `[80]` — equal is not backwards — while two
    /// sentences of this class's own documentation promised that it could, one calling a repeated
    /// index caught and the other naming same-minute double allocation as the very population being
    /// handed to a human. The purpose line says "consuming an index that a later entry will collide
    /// with"; the collision itself was the shape it could not see.
    ///
    /// It is also the MAJORITY shape, which is what makes it a defect rather than a wording slip: on
    /// this machine, adjacent EQUAL pairs outnumber adjacent DECREASING ones 320 to 125 across the
    /// live channels (rev-8's count). And it is the founding incident — CLAUDE.md decision 12 records
    /// `option-lab-2` carrying two `[80]` and two `[81]`.
    /// </summary>
    public static IReadOnlyList<ChannelIndexCrossing> Find_Crossings(IReadOnlyList<ChannelHeaderLine> headers)
    {
        List<ChannelIndexCrossing> crossings = [];

        for (var i = 1; i < headers.Count; i++)
        {
            if (headers[i].Index <= headers[i - 1].Index)
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
    ///
    /// A REPEAT IS NAMED AS A REPEAT. `Find_Crossings` stopped being strict so that `[80]`, `[80]`
    /// would surface at all — 72% of the anomalies on this machine and the founding incident of
    /// decision 12 — and "index runs backwards" is simply false about that pair. A line that describes
    /// the majority shape wrongly is the same defect as the docstring that claimed it was caught.
    /// </summary>
    public static string Describe_Crossing(ChannelIndexCrossing crossing)
    {
        var movement = crossing.Later.Index == crossing.Earlier.Index ? "index REPEATS" : "index runs backwards";

        return
            $"{movement}: [{crossing.Earlier.Index}] then [{crossing.Later.Index}]. "
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
    ///
    /// TEXT ONLY, AND THE EXCLUSIONS ARE THE WHOLE POINT. This key used to carry
    /// <see cref="ChannelHeaderLine.Source"/> and <see cref="ChannelHeaderLine.LineNumber"/>, and
    /// BOTH change when <see cref="Channel_Compactor"/> moves entries from the live file into the
    /// `.archive.md` sibling — the same pair of lines, re-keyed by a move nobody wrote. A crossing
    /// absorbed as history was then NEW again on the next sweep, with the channel no longer at first
    /// sight, so it logged: the waterfall arriving by the one route the old test did not cover, since
    /// it pinned stability under APPENDS only (rev-8 F5, 2026-08-13). Compaction is not exotic here —
    /// decision 13 exists because it runs on these channels routinely.
    ///
    /// The line TEXT is what does not move, so the key is the two lines and nothing else. The
    /// separator is a NEWLINE because a header line provably cannot contain one — the parser splits
    /// on it — so no pair of lines can be re-cut to forge another pair's key. Any printable separator
    /// can appear inside a subject and is forgeable.
    /// </summary>
    public static string Build_DedupeKey(ChannelIndexCrossing crossing)
    {
        return $"{crossing.Earlier.Line}\n{crossing.Later.Line}";
    }
}
