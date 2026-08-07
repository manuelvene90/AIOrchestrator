using AIOrchestratorCoreLib.Planning;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning;

public class PlanLedgerParserTests
{
    [Fact]
    public void Parse_CountsAllMarkers_AndPicksTheInProgressTaskAsCurrent()
    {
        var progress = PlanLedger_Parser.Parse_OrNull("""
            # PLAN — rebuild the login flow

            - [x] 1. map the legacy behavior
            - [x] 2. design the new flow
            - [>] 3. implement the token exchange
            - [ ] 4. wire the UI
            - [!] 5. deploy (waiting on owner)

            Notes: anything non-task is ignored.
            """);

        Assert.NotNull(progress);
        Assert.Equal(2, progress.Done);
        Assert.Equal(1, progress.InProgress);
        Assert.Equal(1, progress.Blocked);
        Assert.Equal(5, progress.Total);
        Assert.Equal("3. implement the token exchange", progress.CurrentTaskText);
    }

    [Fact]
    public void Parse_NoInProgressTask_FallsBackToTheFirstOpenOne()
    {
        var progress = PlanLedger_Parser.Parse_OrNull("- [x] done thing\n- [ ] next thing\n- [ ] later thing");

        Assert.NotNull(progress);
        Assert.Equal("next thing", progress.CurrentTaskText);
    }

    [Fact]
    public void Parse_NoTaskLines_ReturnsNull()
    {
        Assert.Null(PlanLedger_Parser.Parse_OrNull("# just a header\nprose only"));
        Assert.Null(PlanLedger_Parser.Parse_OrNull(""));
    }
}
