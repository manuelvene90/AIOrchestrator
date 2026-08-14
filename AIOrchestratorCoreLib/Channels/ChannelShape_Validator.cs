using System.Text.RegularExpressions;

namespace AIOrchestratorCoreLib.Channels;

/// <summary>
/// Finds lines that were MEANT to be entry headers but do not parse as one.
///
/// WHY THIS EXISTS: a malformed header does not degrade gracefully — the entry becomes INVISIBLE.
/// <see cref="ChannelEntry_Parser"/> folds those lines into the previous entry's body, so the app
/// never mirrors it to Telegram (the owner never sees the message at all), never resolves state
/// from it, never counts it in the idle detector, and `Get_NextIndex` keeps handing out an index
/// that is already taken. Nothing errors; the message simply does not exist as far as the system
/// is concerned.
///
/// Observed live on 2026-08-07, all from one supervisor, in three shapes:
///   `## [SUPERVISOR — 2026-08-08 04:14] SWITCH YOUR WATCHER: …`   (no index, no FROM)
///   `## [supervisor] FROM supervisor — … — CHECK YOUR WATCHER …`  (non-numeric index)
///   `## [2b] FROM supervisor — … — Excellent pass …`              (non-numeric index)
///
/// The distinction that matters: an ordinary markdown heading inside a body (`## BRANCH SUMMARY —
/// …`) is NOT a malformed header and must never be flagged. A line counts as an ATTEMPTED header
/// only if it opens a bracket like a header does, or names an author with FROM.
/// </summary>
public static partial class ChannelShape_Validator
{
    public const string CANONICAL_FORMAT = "## [n] FROM <author> — YYYY-MM-DD HH:mm — <subject>";

    /// <summary>Malformed header lines, as (1-based line number, the offending line).</summary>
    public static IReadOnlyList<(int LineNumber, string Line)> Find_MalformedHeaders(string channelText)
    {
        List<(int LineNumber, string Line)> malformed = [];

        if (string.IsNullOrEmpty(channelText))
            return malformed;

        var lines = channelText.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');

            if (!Looks_LikeAttemptedHeader(line))
                continue;

            if (ChannelEntry_Parser.Is_HeaderLine(line))
                continue;

            malformed.Add((i + 1, line.Trim()));
        }

        return malformed;
    }

    /// <summary>
    /// An attempted header opens with the index bracket, or names the author with FROM right where
    /// a header would.
    ///
    /// It deliberately does NOT scan the whole line for "from". That is what the first version did,
    /// and it flagged ordinary report headings — "## B6. Can deployment rules be built from existing
    /// condition objects?" was reported to the owner as a malformed entry header. A false positive
    /// here is worse than useless: it teaches the reader to ignore the warning, and the warning
    /// exists to catch a silent, invisible failure.
    /// </summary>
    static bool Looks_LikeAttemptedHeader(string line)
    {
        return Attempted_Header_Regex().IsMatch(line);
    }

    [GeneratedRegex(@"^##\s+(\[[^\]]*\]|FROM\s)", RegexOptions.IgnoreCase)]
    private static partial Regex Attempted_Header_Regex();

    /// <summary>
    /// How many bytes of the offending line are dumped. The header prefix is what decides the
    /// verdict, so the first ~96 bytes carry the evidence; a whole subject line would put kilobytes
    /// of hex into the log for nothing.
    /// </summary>
    const int DIAGNOSTIC_BYTE_CAP = 96;

    /// <summary>
    /// What the app SAW, for the log — the bytes, the length, and which of the two regexes matched.
    ///
    /// <para>
    /// WHY BYTES AND NOT TEXT. Twice on 2026-08-13 a well-formed header was flagged, and the report
    /// quoted a line that read as complete and canonical — em-dashes, plain index, single spaces. The
    /// leading hypothesis was a read landing mid-append, which would have to produce a TRUNCATED
    /// capture; both captures were complete, so it was refuted twice and the cause is still unknown.
    /// </para>
    /// <para>
    /// The investigation stalled because the only evidence was TEXT, and text is exactly what cannot
    /// separate these cases: a complete line and a line that merely renders complete look identical.
    /// The instrument could not see what it was measuring. This is that gap closed — the next
    /// occurrence carries its own proof instead of another argument.
    /// </para>
    /// </summary>
    public static string Diagnose(string line)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(line);
        var shown = Math.Min(bytes.Length, DIAGNOSTIC_BYTE_CAP);

        return $"len={bytes.Length}B attempted={Looks_LikeAttemptedHeader(line)} parses={ChannelEntry_Parser.Is_HeaderLine(line)} hex[0..{shown}]={Convert.ToHexString(bytes, 0, shown)}";
    }

    /// <summary>
    /// The key a caller remembers a reported header by, scoped to its channel so two channels carrying
    /// the same line are two facts.
    ///
    /// ONE COMPOSITION, because there were two: the engine's sweep and the baseline pass each spelled
    /// this out, byte-identical, with nothing holding them together. A memo keyed even slightly
    /// differently from the one that reads it never matches, so every header would be reported for ever
    /// — and that failure looks exactly like the invisible-entry bug the memo exists to stop
    /// (decision 12, rev-10 F3).
    /// </summary>
    public static string Build_MemoKey(string channelFilePath, string headerLine)
    {
        return $"{channelFilePath}|{headerLine}";
    }

    /// <summary>The channel entry the app posts when it finds one — it must say what to do, not just complain.</summary>
    public static string Build_ReportBody(IReadOnlyList<(int LineNumber, string Line)> malformed)
    {
        var lines = malformed.Select(m => $"  line {m.LineNumber}: {m.Line}");

        return
            $"These lines look like entry headers but do not parse as one, so the app CANNOT SEE those entries: they were never mirrored to the owner's phone, never counted as traffic, and their index numbers are still free.\n\n"
            + string.Join('\n', lines)
            + $"\n\nThe only header the app recognises is:\n\n  {CANONICAL_FORMAT}\n\n"
            + "The index must be a plain number, the author word must follow FROM, and both separators must be em-dashes. Re-append anything important above as a NEW, correctly-formed entry — never edit the malformed line, the channel is append-only.";
    }
}
