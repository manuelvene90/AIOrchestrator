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
/// ALL OF THAT IS TRUE OF A GENUINELY MALFORMED HEADER, AND THIS DETECTOR CANNOT TELL ONE FROM A
/// LINE READ MID-WRITE. It reads the whole file with no cursor and no second look, so a caller that
/// catches a long append in progress sees `## [21] FROM` — attempted, not parseable — and is not
/// wrong about the bytes it read, only about what they meant. The tailer, which buffers an
/// incomplete line until the rest arrives, never sees the same thing.
///
/// That is why <see cref="Build_ReportBody"/> reports the LINE and not the consequences: the
/// consequences depend on which of the two cases this is, and this component cannot tell.
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
    /// The channel entry the app posts when it finds one — it must say what to do, not just complain.
    ///
    /// IT STATES ONLY WHAT IT OBSERVED. It used to open by asserting three consequences it never
    /// computed — that the entries "were never mirrored to the owner's phone, never counted as
    /// traffic, and their index numbers are still free" — as a string literal, with the app's
    /// authority, in the channel a supervisor reads to decide what to do.
    ///
    /// Those consequences hold for a header that is GENUINELY malformed. They do not hold for the
    /// case this detector cannot distinguish from it: a line read MID-WRITE. The tailer buffers an
    /// incomplete line and delivers the entry when the rest arrives, so for a torn read all three
    /// claims are false — and on 2026-08-13 a supervisor believed them about its own entry,
    /// re-appended a duplicate, and spent three exchanges hunting a cause that did not exist.
    ///
    /// This is the same defect as the guard alert's deleted tail, four hours apart in the same
    /// evening and the same subsystem: that one invented a CAUSE, this one invented CONSEQUENCES.
    /// Decision 21's rule for guards, applied to reports — never state an answer you did not compute.
    ///
    /// The next free index IS computed, by the caller, from the same text this was found in — so it
    /// is stated. It is also the actionable half: a writer re-appending needs that number, and
    /// guessing it is how two entries end up sharing an index.
    /// </summary>
    public static string Build_ReportBody(IReadOnlyList<(int LineNumber, string Line)> malformed, int nextFreeIndex)
    {
        var lines = malformed.Select(m => $"  line {m.LineNumber}: {m.Line}");

        return
            $"These lines look like entry headers but do not parse as one:\n\n"
            + string.Join('\n', lines)
            + $"\n\nThe only header the app recognises is:\n\n  {CANONICAL_FORMAT}\n\n"
            + $"The index must be a plain number, the author word must follow FROM, and both separators must be em-dashes. The next unused index on this channel is [{nextFreeIndex}]. "
            + "Re-append anything important above as a NEW, correctly-formed entry — never edit the malformed line, the channel is append-only.";
    }
}
