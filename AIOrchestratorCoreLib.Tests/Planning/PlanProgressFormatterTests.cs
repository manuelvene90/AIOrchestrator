using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Planning.PlanProgress;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning;

/// <summary>
/// The completion figure the owner reads on /status, /progress and the periodic push. One wording
/// for all three, so they cannot quote different numbers for the same ledger.
/// </summary>
public class PlanProgressFormatterTests
{
    [Fact]
    public void Describe_Counts_LeadsWithDoneOutOfTotalAndAPercentage()
    {
        Assert.Equal("57/76 done (75%)", PlanProgress_Formatter.Describe_Counts(Build(done: 57, total: 76)));
    }

    [Fact]
    public void Describe_Counts_AppendsRunningAndBlockedOnlyWhenThereAreSome()
    {
        Assert.Equal(
            "10/20 done (50%) · 2 running · 1 BLOCKED",
            PlanProgress_Formatter.Describe_Counts(Build(done: 10, total: 20, inProgress: 2, blocked: 1)));

        Assert.Equal("10/20 done (50%)", PlanProgress_Formatter.Describe_Counts(Build(done: 10, total: 20)));
    }

    /// <summary>
    /// Truncation, not rounding: 75 of 76 is 98.68%, and showing "100%" for unfinished work would
    /// make the only figure that must be trustworthy the one that lies.
    /// </summary>
    [Fact]
    public void Describe_Counts_TruncatesSoOnly_AllDone_Reads100()
    {
        Assert.Equal("75/76 done (98%)", PlanProgress_Formatter.Describe_Counts(Build(done: 75, total: 76)));
        Assert.Equal("76/76 done (100%)", PlanProgress_Formatter.Describe_Counts(Build(done: 76, total: 76)));
    }

    [Fact]
    public void Describe_Counts_OmitsThePercentageForAnEmptyLedger()
    {
        Assert.Equal("0/0 done", PlanProgress_Formatter.Describe_Counts(Build(done: 0, total: 0)));
    }

    static IPlanProgress Build(int done, int total, int inProgress = 0, int blocked = 0)
    {
        return PlanProgress_Factory.Create(done, inProgress, blocked, total, null);
    }
}
