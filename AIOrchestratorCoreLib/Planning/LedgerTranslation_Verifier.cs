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
    /// WHAT changed about the ledger's shape, or null if nothing did: same number of lines, and the
    /// same marker on each one, in the same order. Everything else is prose and is free to change —
    /// that is what was being asked for.
    ///
    /// IT NAMES THE CHANGE RATHER THAN ANSWERING YES OR NO, and that is not decoration. Rule 15
    /// correctly forbids telling the owner about a fallback they cannot act on, which makes the log
    /// line the ENTIRE diagnostic surface for the one failure this exists to detect. A bare bool
    /// leaves that line unable to say whether a row vanished or a marker was rewritten, and rule 21
    /// names that shape exactly: "hook error" is the silence again.
    ///
    /// PER INDEX, not as a tally: rows regrouped ACROSS marker groups keep every count identical, and
    /// a set comparison would call that preserved while the owner's document had been rearranged.
    /// **Two rows carrying the SAME marker that swap places are not caught** — their markers match at
    /// both indexes — so what is guaranteed is the marker SEQUENCE, not the row order. Worth stating
    /// because an overstated guarantee here survives until someone re-derives it.
    ///
    /// "No marker" is itself a shape and is checked like any other. The heading carries none, so it
    /// may be translated freely — and a heading that comes back WEARING one means the model read the
    /// list and wrote another row into it.
    /// </summary>
    public static string? Describe_ShapeChange_OrNull(string original, string translated)
    {
        if (string.IsNullOrWhiteSpace(translated))
            return "the answer came back empty";

        // Trailing whitespace is not structure. The translator trims its own output and a subprocess
        // may append a newline; treating that as a shape change would fall back to English on every
        // call — a silent loss of the Italian layer wearing the costume of a guard.
        var originalLines = original.TrimEnd().Split('\n');
        var translatedLines = translated.TrimEnd().Split('\n');

        if (originalLines.Length != translatedLines.Length)
            return Describe_LineCountChange(originalLines, translatedLines);

        for (var index = 0; index < originalLines.Length; index++)
        {
            var originalMarker = Read_Marker_OrEmpty(originalLines[index]);
            var translatedMarker = Read_Marker_OrEmpty(translatedLines[index]);

            if (originalMarker == translatedMarker)
                continue;

            // The line NUMBER, because "a marker changed" in a forty-row ledger is the same silence
            // one level down.
            return $"line {index + 1}: marker '{originalMarker}' came back as '{translatedMarker}'";
        }

        return null;
    }

    /// <summary>
    /// WHICH KIND of line count change, because the one diagnostic line has to tell a lost ROW from a
    /// reflowed blank separator and the raw numbers cannot.
    ///
    /// Every topic-scope /progress carries an interior blank line by construction —
    /// `Build_OrchestrationLedgerText` joins the counts line, a blank, then the rows — so a model that
    /// merely closes that gap was reported as "5 lines came back as 4", which reads as a vanished
    /// ledger row and sends whoever is diagnosing it looking for the wrong thing.
    /// </summary>
    static string Describe_LineCountChange(string[] originalLines, string[] translatedLines)
    {
        var originalBlanks = Count_BlankLines(originalLines);
        var translatedBlanks = Count_BlankLines(translatedLines);

        var blankPart = originalBlanks == translatedBlanks
            ? ""
            : $" (blank separators {originalBlanks} → {translatedBlanks})";

        return $"{originalLines.Length} lines came back as {translatedLines.Length}{blankPart}";
    }

    static int Count_BlankLines(string[] lines)
    {
        var blanks = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                blanks++;
        }

        return blanks;
    }

    /// <summary>
    /// The four characters a rendered ledger row opens with, or empty for a line that carries no
    /// marker — which is prose in every message this repo builds, though the rule is structural and a
    /// translated line is arbitrary model output.
    ///
    /// TWO VOCABULARIES ARE CHECKED, and there is a THIRD SHAPE with no markers at all. /progress in a
    /// topic renders `[x] row` and /tasks renders `  x row`; /progress in GENERAL renders one counts
    /// line per orchestration and no rows whatever, so there the marker half finds nothing and the
    /// check really is a bare line count. That is unavoidable — with no markers there is nothing to
    /// compare — but it was previously written as though knowing both vocabularies covered every
    /// message, and an overstated guarantee here survives until someone re-derives it.
    ///
    /// It is read as literal characters rather than parsed, because what is being compared is whether
    /// the SAME thing came back. `[X]` for `[x]`, or `[]` for `[ ]`, is the ledger's vocabulary going
    /// out in two spellings, and a parse that normalised them would agree that nothing had changed.
    ///
    /// THE TRAILING SPACE IS OPTIONAL AT END OF LINE, and that is not tidiness. `- [ ]` is a supported
    /// ledger row: the parser accepts it, counts it, and both formatters render it as a four-character
    /// line whose last character is the space before an empty task text. Requiring that space made a
    /// model returning the visually identical three-character line a shape change — so one placeholder
    /// row in a PLAN.md pinned that orchestration to English for every /progress and /tasks from then
    /// on, with no owner-visible reason, because the trigger is a property of the FILE rather than of
    /// one sample. Whole-string trimming does not reach it unless it is the last line.
    /// </summary>
    static string Read_Marker_OrEmpty(string line)
    {
        const int MARKER_LENGTH = 4;
        const int MARKER_BODY = 3;

        if (line.Length < MARKER_BODY)
            return "";

        // The space is required only when something follows it: a row with empty task text ends at
        // the marker, and its trailing space is not structure the model has to preserve.
        if (line.Length >= MARKER_LENGTH && line[3] != ' ')
            return "";

        var isProgressMarker = line[0] == '[' && line[2] == ']';
        var isFullFormMarker = line[0] == ' ' && line[1] == ' ' && line[2] != ' ';

        // The BODY, never the trailing space, so `[x] row` and a bare `[x]` yield the same marker and
        // an empty row cannot disagree with itself about what it is.
        return isProgressMarker || isFullFormMarker ? line[..MARKER_BODY] : "";
    }
}
