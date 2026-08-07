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
public static class ChannelShape_Validator
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

    static bool Looks_LikeAttemptedHeader(string line)
    {
        if (!line.StartsWith("## ", StringComparison.Ordinal))
            return false;

        var afterHashes = line[3..].TrimStart();

        // '## [' is the header's own opening; ' FROM ' names an author. A heading with neither is
        // ordinary body markdown and none of our business.
        return afterHashes.StartsWith('[') || line.Contains(" FROM ", StringComparison.OrdinalIgnoreCase);
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
