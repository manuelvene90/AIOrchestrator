using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Planning.PlanBackend;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning.PlanBackend;

/// <summary>
/// WHAT THE WRITER REFUSES TO DO, and why refusing beats guessing: every case here used to produce a
/// plan that looked fine and a round trip that lied — a request reported delivered for work finished
/// last week, or a ledger line in the owner's denominator with no request to trace to.
/// </summary>
public class PlanRequestWriterRefusalTests
{
    static readonly DateTime WHEN = new(2026, 9, 6, 14, 30, 0, DateTimeKind.Local);

    const string PLAN = """
        # PLAN — repo (orch-1)

        - [x] update the README
        - [>] build export
          - [ ] add the export button

        ## PARKED — found, not asked for

        """;

    /// <summary>
    /// A title that names a line already marked DONE. Attaching to it would make the very next pass
    /// report that request closed upstream, with evidence, for work that finished before the request
    /// existed — the round trip's worst possible failure, and it needs no crash to happen.
    /// </summary>
    [Fact]
    public void ARequestNamingAnAlreadyDoneLineIsRefused()
    {
        var write = PlanRequest_Writer.Write_Request(
            PLAN,
            new ApprovedPlanRequest("FIN-1", "update the README", "please update the README"),
            WHEN);

        Assert.Equal(PlanRequestWriteOutcomes.Refused, write.Outcome);
        Assert.False(write.Trackable);
        Assert.Contains("already exists in the ledger marked [x]", write.Refusal);
        Assert.Equal(PLAN, write.PlanText);
    }

    /// <summary>
    /// A title matching a SUB-TASK must not suppress the top-level line. The closure check never looks
    /// at sub-tasks, so the request would have been tracked against a line that could never be seen to
    /// close — owed for ever, retried on every pass.
    /// </summary>
    [Fact]
    public void ASubTaskWithTheSameTextDoesNotStandInForTheLedgerLine()
    {
        var write = PlanRequest_Writer.Write_Request(
            PLAN,
            new ApprovedPlanRequest("FIN-2", "add the export button", "I want an export button"),
            WHEN);

        Assert.Equal(PlanRequestWriteOutcomes.Written, write.Outcome);

        var progress = PlanLedger_Parser.Parse_OrNull(write.PlanText);

        Assert.Contains(PlanLedger_Lines.Top_Level(progress!), line => line.Text == "add the export button");
    }

    [Fact]
    public void ARequestWithNoTitleIsRefusedAndSaysSo()
    {
        var write = PlanRequest_Writer.Write_Request(PLAN, new ApprovedPlanRequest("FIN-3", "   ", "  "), WHEN);

        Assert.Equal(PlanRequestWriteOutcomes.Refused, write.Outcome);
        Assert.Contains("no title", write.Refusal);
    }

    [Fact]
    public void ARequestWithNoIdIsRefusedAndSaysSo()
    {
        var write = PlanRequest_Writer.Write_Request(PLAN, new ApprovedPlanRequest("", "a real title", "words"), WHEN);

        Assert.Equal(PlanRequestWriteOutcomes.Refused, write.Outcome);
        Assert.Contains("no id", write.Refusal);
    }

    /// <summary>
    /// The row is found by a DELIMITED token, not by the bare id anywhere in the line. An id of "42"
    /// used to match the clock in "| 1 | 09:42 | … |", so the table row was judged present and skipped
    /// while the ledger line was still written — a line in the owner's denominator with nothing to
    /// trace to, which is decision 22 inverted.
    /// </summary>
    [Fact]
    public void AShortIdDoesNotMatchATimestampInAnEarlierRow()
    {
        const string WITH_TABLE = """
            # PLAN — repo (orch-1)

            - [>] something

            ## OWNER REQUESTS — written the moment they arrive, in arrival order, never deleted

            | # | when | what they asked for | status |
            |---|---|---|---|
            | 1 | 09:42 | an earlier ask | handled |

            """;

        var write = PlanRequest_Writer.Write_Request(WITH_TABLE, new ApprovedPlanRequest("42", "a new ask", "a new ask"), WHEN);

        Assert.Equal(PlanRequestWriteOutcomes.Written, write.Outcome);
        Assert.Equal(2, write.OwnerRequestNumber);
        Assert.Contains($"{PlanRequest_Writer.UPSTREAM_TOKEN_PREFIX}42", write.PlanText);
    }

    /// <summary>And an id that is a PREFIX of one already ingested gets its own row too.</summary>
    [Fact]
    public void AnIdThatIsAPrefixOfAnIngestedOneStillGetsItsOwnRow()
    {
        var first = PlanRequest_Writer.Write_Request(PLAN, new ApprovedPlanRequest("REQ-11", "first ask", "first ask"), WHEN);
        var second = PlanRequest_Writer.Write_Request(first.PlanText, new ApprovedPlanRequest("REQ-1", "second ask", "second ask"), WHEN);

        Assert.Equal(PlanRequestWriteOutcomes.Written, second.Outcome);
        Assert.Equal(2, second.OwnerRequestNumber);
    }

    /// <summary>A pipe in the ID shears the table exactly as one in the words would. Escaped too.</summary>
    [Fact]
    public void APipeInTheIdDoesNotShearTheTable()
    {
        var write = PlanRequest_Writer.Write_Request(PLAN, new ApprovedPlanRequest("A|B", "an ask", "an ask"), WHEN);

        var row = write.PlanText.Split('\n').Single(line => line.StartsWith("| 1 |"));

        Assert.Equal(5, row.Replace("\\|", string.Empty).Count(character => character == '|'));
    }

    /// <summary>
    /// A CRLF plan comes back CRLF. Mixed endings are not a parse failure, but they make every diff of
    /// the owner's own file noisy for no reason.
    /// </summary>
    [Fact]
    public void ACrlfPlanKeepsItsLineEndings()
    {
        var crlf = PLAN.Replace("\n", "\r\n");

        var write = PlanRequest_Writer.Write_Request(crlf, new ApprovedPlanRequest("FIN-4", "a new ask", "a new ask"), WHEN);

        Assert.Equal(PlanRequestWriteOutcomes.Written, write.Outcome);
        Assert.DoesNotContain(write.PlanText.Split("\r\n"), line => line.Contains('\n'));
        Assert.Contains("- [ ] a new ask\r", write.PlanText);
    }

    /// <summary>
    /// The status cell is rewritten when the line closes, so an app-written row does not answer "nobody
    /// is on this" at every check-in for the rest of the orchestration.
    /// </summary>
    [Fact]
    public void AClosedRowsStatusIsRewritten()
    {
        var write = PlanRequest_Writer.Write_Request(PLAN, new ApprovedPlanRequest("FIN-5", "an ask", "their words"), WHEN);

        var (updated, changed) = PlanRequest_Writer.Set_RowStatus(
            write.PlanText,
            "FIN-5",
            PlanRequest_Writer.Describe_ReportedStatus("FIN-5"));

        Assert.True(changed);
        Assert.Contains("done in the ledger, reported upstream", updated);
        Assert.DoesNotContain("in the ledger, not started", updated);

        // The owner's words and the row number are untouched — only the status cell moves.
        var row = updated.Split('\n').Single(line => line.StartsWith("| 1 |"));

        Assert.Contains("their words", row);
    }

    /// <summary>A row somebody deleted is not re-added: the file belongs to whoever runs the orchestration.</summary>
    [Fact]
    public void SettingTheStatusOfAMissingRowChangesNothing()
    {
        var (updated, changed) = PlanRequest_Writer.Set_RowStatus(PLAN, "FIN-9", "done");

        Assert.False(changed);
        Assert.Equal(PLAN, updated);
    }
}
