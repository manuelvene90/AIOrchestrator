using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Planning.PlanBackend;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning.PlanBackend;

/// <summary>
/// WHERE AN UPSTREAM REQUEST LANDS IN A PLAN, and it is two places for one reason each: a row in
/// OWNER REQUESTS because that table is the record of the ask, and a line in the ledger because a
/// prose "status" cell is not a signal anything can read — the one closure signal this codebase has
/// is a marker turning <c>x</c>.
/// </summary>
public class PlanRequestWriterTests
{
    const string SEEDED_PLAN = """
        # PLAN — repo (orch-1)

        - [x] the first thing
        - [>] the second thing

        ## PARKED — found, not asked for

        - the tailer's retry count is unbounded — imp-2

        ## OWNER REQUESTS — written the moment they arrive, in arrival order, never deleted

        | # | when | what they asked for | status |
        |---|---|---|---|
        | 4 | 12:51 | /left must use the bracket format too | handled |

        """;

    static readonly ApprovedPlanRequest REQUEST = new("FIN-D-275", "add the export button", "I want an export button on the report page");

    static readonly DateTime WHEN = new(2026, 9, 6, 14, 30, 0, DateTimeKind.Local);

    /// <summary>
    /// The ledger line goes at the END OF THE LEDGER — above the first non-ledger heading, never at
    /// the end of the file, where PARKED or OWNER REQUESTS would swallow it and the owner's bar would
    /// never see it. That stranding is the exact defect PlanShape_Validator complains about.
    /// </summary>
    [Fact]
    public void TheLedgerLineLandsInTheLedger()
    {
        var write = PlanRequest_Writer.Write_Request(SEEDED_PLAN, REQUEST, WHEN);

        Assert.True(write.Changed);
        Assert.Equal("add the export button", write.LedgerRowRef);

        var progress = PlanLedger_Parser.Parse_OrNull(write.PlanText);

        Assert.NotNull(progress);
        Assert.Equal(3, progress!.Total);
        Assert.Contains(progress.Lines, line => line.Text == "add the export button" && line.Marker == " ");
    }

    /// <summary>
    /// And the plan it produces draws no complaint from the validator — the app must not write a shape
    /// it would then advise the supervisor to fix.
    /// </summary>
    [Fact]
    public void TheWrittenPlanIsAShapeTheAppApprovesOf()
    {
        var write = PlanRequest_Writer.Write_Request(SEEDED_PLAN, REQUEST, WHEN);

        Assert.Empty(PlanShape_Validator.Find_UnrepresentableLines(write.PlanText));
    }

    /// <summary>
    /// The table row carries the owner's OWN WORDS, the arrival time, and the upstream id in its
    /// status — the id because that is what makes a second ingestion recognisable, the words because
    /// "their words, not your restatement" is the table's whole rule.
    /// </summary>
    [Fact]
    public void TheTableRowCarriesTheirWordsAndTheUpstreamId()
    {
        var write = PlanRequest_Writer.Write_Request(SEEDED_PLAN, REQUEST, WHEN);

        var row = write.PlanText.Split('\n').Single(line => line.StartsWith("| 5 |"));

        Assert.Contains("14:30", row);
        Assert.Contains("I want an export button on the report page", row);
        Assert.Contains("FIN-D-275", row);
        Assert.Equal(5, write.OwnerRequestNumber);
    }

    /// <summary>
    /// NEVER RENUMBERED. The existing row keeps the number other text refers to; the new one is one
    /// past the highest, not one past the count — a table whose rows were hand-edited still numbers
    /// forward.
    /// </summary>
    [Fact]
    public void ExistingRowsAreLeftExactlyAsTheyWere()
    {
        var write = PlanRequest_Writer.Write_Request(SEEDED_PLAN, REQUEST, WHEN);

        Assert.Contains("| 4 | 12:51 | /left must use the bracket format too | handled |", write.PlanText);
    }

    /// <summary>The bar does not move: an OWNER REQUESTS row is invisible to the parser by design.</summary>
    [Fact]
    public void TheTableRowItselfIsNotCountedTwice()
    {
        var write = PlanRequest_Writer.Write_Request(SEEDED_PLAN, REQUEST, WHEN);
        var progress = PlanLedger_Parser.Parse_OrNull(write.PlanText);

        // Three: the two that were there plus the one ledger line — not four.
        Assert.Equal(3, progress!.Total);
    }

    /// <summary>
    /// A plan with no OWNER REQUESTS section — every plan seeded before the table existed — grows one,
    /// with the heading and header the role commands teach.
    /// </summary>
    [Fact]
    public void APlanWithNoTableGetsOne()
    {
        const string PLAN = """
            # PLAN — repo (orch-1)

            - [>] the only thing

            """;

        var write = PlanRequest_Writer.Write_Request(PLAN, REQUEST, WHEN);

        Assert.Contains(PlanRequest_Writer.OWNER_REQUESTS_HEADING, write.PlanText);
        Assert.Contains(PlanRequest_Writer.TABLE_HEADER_ROW, write.PlanText);
        Assert.Contains("| 1 |", write.PlanText);
        Assert.Equal(1, write.OwnerRequestNumber);
        Assert.Equal(2, PlanLedger_Parser.Parse_OrNull(write.PlanText)!.Total);
    }

    /// <summary>
    /// THE SAME REQUEST TWICE WRITES NOTHING. The step's persisted state is the first guard; this is
    /// the second, derived from the file itself, so a lost state file costs a duplicate acknowledgement
    /// and never a duplicate row in the owner's plan.
    /// </summary>
    [Fact]
    public void WritingTheSameRequestAgainChangesNothing()
    {
        var first = PlanRequest_Writer.Write_Request(SEEDED_PLAN, REQUEST, WHEN);
        var second = PlanRequest_Writer.Write_Request(first.PlanText, REQUEST, WHEN);

        Assert.False(second.Changed);
        Assert.Equal(first.PlanText, second.PlanText);
        Assert.Equal("add the export button", second.LedgerRowRef);
    }

    /// <summary>
    /// A pipe in the owner's words would end the cell and shear the table. Escaped — and the ledger
    /// line, which is not a table, keeps the character as written.
    /// </summary>
    [Fact]
    public void APipeInTheirWordsDoesNotShearTheTable()
    {
        var request = new ApprovedPlanRequest("FIN-D-9", "pipe handling", "run a | b and show the output");

        var write = PlanRequest_Writer.Write_Request(SEEDED_PLAN, request, WHEN);
        var row = write.PlanText.Split('\n').Single(line => line.StartsWith("| 5 |"));

        // Five CELL boundaries, and the sixth pipe is the escaped one inside the third cell.
        Assert.Equal(5, row.Replace("\\|", string.Empty).Count(character => character == '|'));
        Assert.Contains("a \\| b", row);
    }

    /// <summary>
    /// A title spanning two lines becomes one ledger line. Written as-is it would split, and the
    /// second half would not be a task line at all — the request would be half-invisible.
    /// </summary>
    [Fact]
    public void AMultiLineTitleBecomesOneLedgerLine()
    {
        var request = new ApprovedPlanRequest("FIN-D-10", "export\nthe report", "export\nthe report");

        var write = PlanRequest_Writer.Write_Request(SEEDED_PLAN, request, WHEN);

        Assert.Equal("export the report", write.LedgerRowRef);
        Assert.Contains(PlanLedger_Parser.Parse_OrNull(write.PlanText)!.Lines, line => line.Text == "export the report");
    }
}
