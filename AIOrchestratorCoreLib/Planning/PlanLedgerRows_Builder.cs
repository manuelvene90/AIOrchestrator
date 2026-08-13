using AIOrchestratorCoreLib.Planning.PlanProgress;

namespace AIOrchestratorCoreLib.Planning;

/// <summary>
/// THE LEDGER AS ROWS TO DRAW, from the parsed ledger — every line, in the file's own order, each
/// mapped to a glyph and a palette key.
///
/// IT LIVES HERE BECAUSE THE SUITE CANNOT REACH THE WINDOW (rev-7 L1). The test project references
/// `AIOrchestratorCoreLib` and nothing else; the WPF project is `net10.0-windows` and is not
/// referenced, so anything decided inside `OrchestrationDetailWindow` is unpinnable by construction.
/// The commit that removed that window's three re-implementations of shared logic was therefore not
/// pinned against recurrence — and recurrence is exactly what had happened there. rev-7's refutation
/// is the fix, verbatim: *"the untestability is a placement choice, not a constraint."*
///
/// WHAT THIS CAN AND CANNOT PIN, said plainly so nobody reads more into it. It pins that every marker
/// the parser accepts becomes a row, that a dropped line is drawn as dropped, and that a marker with
/// no mapping stops loudly. **It cannot pin that the window calls it** — that call site is in the
/// unreachable project, and no test in this suite can see it. What the extraction does is shrink the
/// unreachable surface to a two-line adapter, which is as far as placement can go.
/// </summary>
public static class PlanLedgerRows_Builder
{
    public const string DONE_GLYPH = "✔";
    public const string IN_PROGRESS_GLYPH = "▶";
    public const string BLOCKED_GLYPH = "■";
    public const string OPEN_GLYPH = "○";

    /// <summary>Distinct from every other glyph on purpose: a dropped line must not read as a done one.</summary>
    public const string NOT_DOING_GLYPH = "⊘";

    /// <summary>Delivered work is dimmed — present, but no longer where attention goes.</summary>
    public const double DONE_OPACITY = 0.55;

    /// <summary>Dimmer still. It was never delivered and it is not owed.</summary>
    public const double NOT_DOING_OPACITY = 0.45;

    public static IReadOnlyList<PlanLedgerRow> Build_Rows(IPlanProgress progress)
    {
        return [.. progress.Lines.Select(Build_Row)];
    }

    public static PlanLedgerRow Build_Row(PlanLedgerLine line)
    {
        return line.Marker switch
        {
            "x" => new PlanLedgerRow(DONE_GLYPH, "AccentCommunicator", line.Text, DONE_OPACITY, IsBold: false),
            ">" => new PlanLedgerRow(IN_PROGRESS_GLYPH, "StateWorking", line.Text, 1.0, IsBold: true),
            "!" => new PlanLedgerRow(BLOCKED_GLYPH, "StateBlocked", line.Text, 1.0, IsBold: true),
            " " => new PlanLedgerRow(OPEN_GLYPH, "StateNew", line.Text, 1.0, IsBold: false),

            // NOT DOING — drawn, and drawn as dropped rather than as done. It is here at all because a
            // marker that removes weight from the denominator is a delete key unless somebody can see
            // it; the screen that shows the ledger whole is the one place it must appear.
            "-" => new PlanLedgerRow(NOT_DOING_GLYPH, "TextSecondary", line.Text, NOT_DOING_OPACITY, IsBold: false),

            // The parser normalises "X" to "x", so this is unreachable today — it is here for the
            // SIXTH marker. Throwing is right: a marker the parser accepts and this switch does not
            // would otherwise render as a blank row, which is the silent omission this branch exists
            // to remove, wearing a different hat.
            _ => throw new ArgumentOutOfRangeException(nameof(line), $"unhandled plan marker '{line.Marker}' for task '{line.Text}'"),
        };
    }
}
