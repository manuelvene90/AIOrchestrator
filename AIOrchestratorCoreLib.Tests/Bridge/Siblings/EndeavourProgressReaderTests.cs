using AIOrchestratorCoreLib.Bridge.Siblings;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.Siblings;

/// <summary>
/// THE ENDEAVOUR BAR IS A SUM OF THE MEMBERS' OWN LEDGERS (spec 2026-09-23 §3.5), parsed by the one parser —
/// so the sections that parser skips are skipped here too, and a closed member stays in, so the bar never
/// goes backwards when a job finishes.
/// </summary>
public class EndeavourProgressReaderTests : IDisposable
{
    readonly EndeavourTree _tree = new();

    public void Dispose()
    {
        _tree.Dispose();
    }

    [Fact]
    public void Sum_AddsEveryCount()
    {
        _tree.Add_Solo("a", "AI-Orch · settings work", "e-1");
        _tree.Add_Solo("b", "AI-Orch · limits work", "e-1");
        Write_Plan("a", "- [x] a1\n- [>] a2\n- [!] a3\n- [-] a4\n- [ ] a5\n");
        Write_Plan("b", "- [x] b1\n- [x] b2\n- [?] b3\n- [>] b4\n- [ ] b5\n- [ ] b6\n");

        var sum = EndeavourProgress_Reader.Sum_OrNull(_tree.Paths, Members());

        Assert.NotNull(sum);
        Assert.Equal(3, sum.Done);
        Assert.Equal(2, sum.InProgress);
        Assert.Equal(2, sum.Blocked);
        Assert.Equal(1, sum.BlockedOnOwner);
        Assert.Equal(1, sum.NotDoing);
        Assert.Equal(10, sum.Total);
        Assert.Equal(["a5", "b5", "b6"], sum.OpenTasks);
        Assert.Equal(["a2", "b4"], sum.InProgressTasks);
        Assert.Equal(["a3", "b3"], sum.BlockedTasks);
        Assert.Equal("a2", sum.CurrentTaskText);
    }

    [Fact]
    public void Sum_IncludesAClosedMember()
    {
        _tree.Add_Solo("a", "AI-Orch · settings work", "e-1");
        _tree.Add_Solo("b", "AI-Orch · limits work", "e-1");
        Write_Plan("a", "- [x] a1\n- [x] a2\n");
        Write_Plan("b", "- [ ] b1\n");
        _tree.Store.Close_Orchestration("a");

        var sum = EndeavourProgress_Reader.Sum_OrNull(_tree.Paths, Members());

        Assert.NotNull(sum);
        Assert.Equal(2, sum.Done);
        Assert.Equal(3, sum.Total);
    }

    [Fact]
    public void Sum_SkipsParkedAndOwnerRequestsSections()
    {
        _tree.Add_Solo("a", "AI-Orch · settings work", "e-1");
        _tree.Add_Solo("b", "AI-Orch · limits work", "e-1");
        Write_Plan("a", "## OWNER REQUESTS\n- [ ] the owner's ask\n## Ledger\n- [x] a1\n");
        Write_Plan("b", "## Ledger\n- [ ] b1\n## PARKED\n- [ ] a finding nobody asked for\n");

        var sum = EndeavourProgress_Reader.Sum_OrNull(_tree.Paths, Members());

        Assert.NotNull(sum);
        Assert.Equal(1, sum.Done);
        Assert.Equal(2, sum.Total);
        Assert.Equal(["b1"], sum.OpenTasks);
    }

    [Fact]
    public void Sum_OfMembersWithNoLedger_IsNull()
    {
        _tree.Add_Solo("a", "AI-Orch · settings work", "e-1");
        _tree.Add_Solo("b", "AI-Orch · limits work", "e-1");
        Write_Plan("b", "no task lines at all, only prose\n");

        Assert.Null(EndeavourProgress_Reader.Sum_OrNull(_tree.Paths, Members()));
    }

    [Fact]
    public void Sum_OfOneLedgerAndOneMissing_IsTheOneLedger()
    {
        _tree.Add_Solo("a", "AI-Orch · settings work", "e-1");
        _tree.Add_Solo("b", "AI-Orch · limits work", "e-1");
        Write_Plan("a", "- [x] a1\n- [ ] a2\n");

        var sum = EndeavourProgress_Reader.Sum_OrNull(_tree.Paths, Members());

        Assert.NotNull(sum);
        Assert.Equal(1, sum.Done);
        Assert.Equal(2, sum.Total);
    }

    IReadOnlyList<IOrchestrationSession> Members()
    {
        return [.. _tree.Sessions().OrderBy(session => session.OrchId, StringComparer.Ordinal)];
    }

    void Write_Plan(string orchId, string text)
    {
        File.WriteAllText(_tree.Paths.Get_PlanFile(orchId), text);
    }
}
