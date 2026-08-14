using AIOrchestratorCoreLib.Planning.PlanProgress;
using AIOrchestratorCoreLib.Planning;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning;

/// <summary>
/// A DISCOVERY NOBODY ASKED FOR CANNOT MOVE THE OWNER'S PROGRESS BAR (owner directive, 2026-08-14).
///
/// The complaint these tests come from: implementers and reviewers find problems around the work
/// while doing the work, those findings become tasks, and the endeavour's horizon explodes — so the
/// things the owner actually asked for are still unfinished hours later, sometimes forgotten. The
/// remedy is a PARKED section: a discovery is written down so it is not lost, and sits outside the
/// denominator so it is visibly not part of what was asked for.
///
/// That only holds if the parser agrees. It matches a task line anywhere in the file, so parking
/// something in the natural shape — `- [ ] the tailer's retry count is unbounded` under a "parked"
/// heading — used to add it to the total. The section would READ as parked and COUNT as owed, which
/// is the original failure with a heading over it.
/// </summary>
public class ParkedSectionIsNotTheLedgerTests
{
    const string PLAN = """
        # PLAN — repo (orch-1)

        - [x] the thing the owner asked for
        - [>] the second thing they asked for

        ## PARKED — found, not asked for

        - [ ] the tailer's retry count is unbounded — imp-2, while reading it for the brief
        - [ ] two copies of the duration wording — rev-1

        ## OWNER REQUESTS — written the moment they arrive, in arrival order, never deleted

        | # | when | what they asked for | status |
        |---|---|---|---|
        | 1 | 12:51 | the thing | done |
        """;

    /// <summary>
    /// The bar counts the two lines the owner asked for and neither of the two parked ones — which is
    /// the whole point: 1 of 2 done, not 1 of 4. Both halves are asserted, because a parser that
    /// counted NOTHING would satisfy "the parked lines are not in the total" as well.
    /// </summary>
    [Fact]
    public void ParkedLinesAreNotOwedWork()
    {
        var progress = PlanLedger_Parser.Parse_OrNull(PLAN);

        Assert.NotNull(progress);
        Assert.Equal(2, progress!.Total);
        Assert.Equal(1, progress.Done);
        Assert.Equal(1, progress.InProgress);

        // And they are not hiding in a bucket either — /progress prints every line it kept, so a
        // parked item leaking into `lines` reaches the owner's phone as work in flight.
        Assert.DoesNotContain(progress.Lines, line => line.Text.Contains("retry count"));
        Assert.DoesNotContain(progress.Lines, line => line.Text.Contains("duration wording"));
    }

    /// <summary>
    /// THE SECTION ENDS AT THE NEXT HEADING, and this is the case a truncation would get wrong. A
    /// supervisor who parks something mid-file must not silently lose every task below it: trading a
    /// bar that over-counts for one that under-counts is not a fix, and the loss would be invisible.
    /// </summary>
    [Fact]
    public void TheLedgerResumesAfterTheParkedSection()
    {
        var progress = PlanLedger_Parser.Parse_OrNull("""
            # PLAN

            - [x] first

            ## Parked questions (ask when the owner is reachable)

            - [ ] should the archive keep the old format?

            ## Back to the work

            - [ ] second
            - [!] third — blocked on: the owner's answer
            """);

        Assert.NotNull(progress);
        Assert.Equal(3, progress!.Total);
        Assert.Equal(1, progress.Done);
        Assert.Equal(1, progress.Blocked);
        Assert.Contains(progress.Lines, line => line.Text.StartsWith("second"));
    }

    /// <summary>
    /// The heading is matched as a PREFIX and case-insensitively, because the sections already
    /// written in the field carry trailing text — "## Parked / not engineering" and "## PARKED —
    /// found, not asked for" are the same section. An exact-title rule would have excluded every one
    /// of them while reading as though it covered them.
    /// </summary>
    [Theory]
    [InlineData("## PARKED — found, not asked for")]
    [InlineData("## Parked / not engineering")]
    [InlineData("### parked questions")]
    [InlineData("## OWNER REQUESTS — written the moment they arrive")]
    public void TheseHeadingsOpenANonLedgerSection(string heading)
    {
        Assert.True(PlanLedger_Sections.Opens_NonLedgerSection(heading));
        Assert.True(PlanLedger_Sections.Is_Heading(heading));
    }

    /// <summary>
    /// And these do not, so the rule cannot swallow the ledger by accident. The last two are the
    /// markdown cases: a `#` with no space after it is not a heading, and a bare `###` names nothing.
    /// </summary>
    [Theory]
    [InlineData("## Hardening — owner approved 19:16")]
    [InlineData("- [ ] park the retry counter")]
    [InlineData("#parked")]
    [InlineData("###")]
    public void TheseDoNot(string line)
    {
        Assert.False(PlanLedger_Sections.Opens_NonLedgerSection(line));
    }

    /// <summary>
    /// A plan that is NOTHING but parked items has no ledger at all, and says so with a null rather
    /// than with a 0/0 bar. The owner asked for nothing yet; an empty bar is the honest answer.
    /// </summary>
    [Fact]
    public void APlanOfOnlyParkedItemsHasNoBar()
    {
        Assert.Null(PlanLedger_Parser.Parse_OrNull("""
            # PLAN

            ## PARKED — found, not asked for

            - [ ] the tailer's retry count is unbounded
            """));
    }
}
