using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Planning.PlanProgress;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning;

/// <summary>
/// rev-7 L1: the commit that stopped the detail window re-implementing the parser, the counts wording
/// and the percentage was itself pinned by nothing. Its +183 test lines covered the marker LEGEND;
/// the delegation — the change the branch existed to make — was unreachable, because the test project
/// references `AIOrchestratorCoreLib` alone and the WPF project is not referenced at all.
///
/// So the decision moved to where a test can reach it, which is rev-7's own refutation taken as the
/// fix: *"the untestability is a placement choice, not a constraint."*
///
/// WHAT THIS DOES NOT DO, so nobody reads it as more than it is: it cannot pin that the WINDOW calls
/// the builder. That call site is still in the unreachable project. The extraction shrinks what is
/// unreachable to a two-line palette lookup — it does not make the suite able to see the window.
/// </summary>
public class PlanLedgerRowsBuilderTests
{
    /// <summary>
    /// EVERY MARKER THE PARSER ACCEPTS GETS A ROW. This is the assertion that was missing: taking the
    /// `[-]` case back out now throws rather than rendering, and before this test nothing anywhere
    /// noticed.
    /// </summary>
    [Fact]
    public void EveryTaughtMarkerBecomesARow()
    {
        foreach (var (marker, meaning) in PlanLedger_Markers.ALL)
        {
            var row = PlanLedgerRows_Builder.Build_Row(new PlanLedgerLine(marker, "a task"));

            Assert.False(string.IsNullOrWhiteSpace(row.Glyph), $"`- [{marker}]` ({meaning}) renders no glyph");
            Assert.False(string.IsNullOrWhiteSpace(row.BrushKey), $"`- [{marker}]` ({meaning}) renders no colour");
            Assert.Equal("a task", row.Text);
        }
    }

    /// <summary>
    /// AND NO TWO MARKERS LOOK ALIKE. A dropped line rendered with the done glyph would be the
    /// original defect wearing a tick: still invisible as a drop, while appearing to be delivered
    /// work — which is worse than the omission, because it inflates rather than hides.
    /// </summary>
    [Fact]
    public void NoTwoMarkersShareAGlyph()
    {
        var glyphs = PlanLedger_Markers.ALL
            .Select(entry => PlanLedgerRows_Builder.Build_Row(new PlanLedgerLine(entry.Marker, "t")).Glyph)
            .ToList();

        Assert.Equal(glyphs.Count, glyphs.Distinct().Count());
    }

    /// <summary>
    /// A DROPPED LINE IS DRAWN AS DROPPED — dimmer than delivered work, never bold, and not sharing
    /// the done colour. The whole reason it appears at all is that a marker which removes weight from
    /// the denominator is a delete key unless somebody can see it.
    /// </summary>
    [Fact]
    public void ANotDoingLineReadsAsDroppedRatherThanDone()
    {
        var dropped = PlanLedgerRows_Builder.Build_Row(new PlanLedgerLine("-", "superseded"));
        var done = PlanLedgerRows_Builder.Build_Row(new PlanLedgerLine("x", "delivered"));

        Assert.Equal(PlanLedgerRows_Builder.NOT_DOING_GLYPH, dropped.Glyph);
        Assert.True(dropped.Opacity < done.Opacity, "a dropped line must not read as more present than a delivered one");
        Assert.NotEqual(done.BrushKey, dropped.BrushKey);
        Assert.False(dropped.IsBold);
    }

    /// <summary>
    /// THE WHOLE LEDGER, IN THE FILE'S ORDER, NOTHING DROPPED. The defect was rows vanishing between
    /// the file and the screen, so the count and the order are the two things worth asserting
    /// end-to-end from real ledger text.
    /// </summary>
    [Fact]
    public void TheWholeLedgerSurvivesTheTripFromText()
    {
        var progress = PlanLedger_Parser.Parse_OrNull(
            """
            - [x] first
            - [-] second, dropped
            - [>] third
            - [ ] fourth
            - [!] fifth
            """);

        var rows = PlanLedgerRows_Builder.Build_Rows(progress!);

        Assert.Equal(5, rows.Count);
        Assert.Equal(["first", "second, dropped", "third", "fourth", "fifth"], rows.Select(row => row.Text));

        // The dropped one is PRESENT, which is the entire finding: the window showed four of these.
        Assert.Contains(rows, row => row.Glyph == PlanLedgerRows_Builder.NOT_DOING_GLYPH);
    }

    /// <summary>
    /// A SIXTH MARKER STOPS LOUDLY rather than rendering blank. A marker the parser accepts and this
    /// switch does not would otherwise draw an empty row — the same silent omission, arriving from
    /// the other side.
    /// </summary>
    [Fact]
    public void AnUnmappedMarkerThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanLedgerRows_Builder.Build_Row(new PlanLedgerLine("?", "t")));
    }
}
