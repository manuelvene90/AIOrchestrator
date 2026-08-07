using AIOrchestratorCoreLib.Planning;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning;

/// <summary>
/// The exact line that broke a live orchestration: seven tasks behind one checkbox, so six real
/// commits rendered as zero movement. Detection is the only fix — no amount of diligence can make
/// an unrepresentable line representable.
/// </summary>
public class PlanShapeValidatorTests
{
    [Fact]
    public void Find_TheLiveFailure_ARangeOfTasksInOneLine()
    {
        var complaints = PlanShape_Validator.Find_UnrepresentableLines(
            "- [>] Remaining tasks 3-9 (gear, settings window, KB, PARTNERS #92)");

        Assert.Single(complaints);
        Assert.Contains("RANGE", Assert.Single(complaints));
    }

    [Theory]
    [InlineData("- [ ] tasks 4-11 of the rebuild")]
    [InlineData("- [x] items 2 – 5")]
    [InlineData("- [>] steps 1—3 done together")]
    public void Find_RangeVariants(string line)
    {
        Assert.Single(PlanShape_Validator.Find_UnrepresentableLines(line));
    }

    [Fact]
    public void Find_ALineListingManyDeliverables()
    {
        var complaints = PlanShape_Validator.Find_UnrepresentableLines(
            "- [ ] wire the gear icon, the settings window, the KB page, and the partners note");

        Assert.Contains("deliverables", Assert.Single(complaints));
    }

    [Fact]
    public void Find_HealthyLedger_ComplainsAboutNothing()
    {
        var complaints = PlanShape_Validator.Find_UnrepresentableLines("""
            # PLAN — rebuild

            - [x] 1. map the legacy behaviour
            - [>] 2. implement the token exchange
            - [ ] 3. wire the settings window
            - [!] 4. deploy (waiting on owner)
            Notes: prose lines are ignored, tasks 1-4 mentioned here must not trip the check.
            """);

        Assert.Empty(complaints);
    }

    [Fact]
    public void Find_ShortTaskWithACoupleOfCommas_IsAccepted()
    {
        // Two clauses is prose, not a hidden list — the check must not cry wolf.
        Assert.Empty(PlanShape_Validator.Find_UnrepresentableLines("- [ ] fix the parser, then re-run the suite"));
    }
}
