namespace AIOrchestratorCoreLib.Planning;

/// <summary>
/// Did the ledger survive the Italian layer? The last step on the owner's directive path, and until
/// 2026-08-13 the only one with no guarantee at all.
///
/// `/progress` and `/tasks` hand the WHOLE message to a `claude -p` subprocess when the Italian layer
/// is on, and nothing checked what came back. So "one line per ledger line, nothing hidden, no cap,
/// no truncation" was proven at the formatter and unproven at the phone — and rule 11 makes the
/// Italian layer persisted and the owner's normal mode, so that IS the production path. A model
/// handed a list of forty rows, several of them near-identical, is being invited to summarise.
///
/// A LEDGER IS A DRAWING, the same reason `MonospaceBlocks_Formatter` lifts fenced blocks out before
/// translation. Two shapes were available and this is the second, chosen deliberately:
///
///   1. Extract the prose after each marker, translate that, reassemble the rows here. Stronger in
///      principle — the markers and the row count cannot move because they never leave. But N rows
///      must go through the subprocess either as N calls (a forty-row ledger is forty `claude -p`
///      launches on a path the owner is waiting on) or as ONE delimited payload whose response must
///      then be split into N parts — and that split can come back wrong for the identical reason the
///      whole-ledger translation can. It relocates the risk unless it is paid for in subprocesses.
///
///   2. THIS. Translate once, then verify the shape came back; on any mismatch send the ENGLISH
///      original. One call, and the guarantee is a PURE function the suite can pin — which decided
///      it: the two findings before this one both landed inside `BridgeEngineModel`, where nothing
///      can be tested at all. A new guarantee belongs where it can be observed.
///
/// It fails VISIBLY and in the safe direction: English is readable, a mangled ledger is not.
/// </summary>
public static class LedgerTranslation_Verifier
{
    /// <summary>
    /// Same number of lines, and the same marker on each one, in the same order. Everything else is
    /// prose and is free to change — that is what was being asked for.
    ///
    /// PER INDEX, not as a tally: rows regrouped by state keep every count identical, and a set
    /// comparison would call that preserved while the owner's document had been rearranged.
    ///
    /// "No marker" is itself a shape and is checked like any other. The heading carries none, so it
    /// may be translated freely — and a heading that comes back WEARING one means the model read the
    /// list and wrote another row into it.
    /// </summary>
    public static bool Is_ShapePreserved(string original, string translated)
    {
        if (string.IsNullOrWhiteSpace(translated))
            return false;

        // Trailing whitespace is not structure. The translator trims its own output and a subprocess
        // may append a newline; treating that as a shape change would fall back to English on every
        // call — a silent loss of the Italian layer wearing the costume of a guard.
        var originalLines = original.TrimEnd().Split('\n');
        var translatedLines = translated.TrimEnd().Split('\n');

        if (originalLines.Length != translatedLines.Length)
            return false;

        for (var index = 0; index < originalLines.Length; index++)
        {
            if (Read_Marker_OrEmpty(originalLines[index]) != Read_Marker_OrEmpty(translatedLines[index]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// The four characters a rendered ledger row opens with, or empty for a line that is prose.
    ///
    /// BOTH VOCABULARIES, because both are translated: /progress renders `[x] row` and /tasks renders
    /// `  x row`. Knowing both is what makes the check mean something for /tasks rather than
    /// degrading to a line count there — and the line count is the weaker half, since a model that
    /// rewrites a marker usually keeps the row.
    ///
    /// It is read as literal characters rather than parsed, because what is being compared is whether
    /// the SAME thing came back. `[X]` for `[x]`, or `[]` for `[ ]`, is the ledger's vocabulary going
    /// out in two spellings, and a parse that normalised them would agree that nothing had changed.
    ///
    /// If a renderer's prefix ever changes without this changing with it, the check quietly weakens
    /// to the line count instead of failing — worth knowing, and the reason the line count is not the
    /// only thing here.
    /// </summary>
    static string Read_Marker_OrEmpty(string line)
    {
        const int MARKER_LENGTH = 4;

        if (line.Length < MARKER_LENGTH || line[3] != ' ')
            return "";

        var isProgressMarker = line[0] == '[' && line[2] == ']';
        var isFullFormMarker = line[0] == ' ' && line[1] == ' ' && line[2] != ' ';

        return isProgressMarker || isFullFormMarker ? line[..MARKER_LENGTH] : "";
    }
}
