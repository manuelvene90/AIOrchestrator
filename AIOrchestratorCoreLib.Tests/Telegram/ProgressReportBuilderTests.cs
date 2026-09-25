using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// GENERAL'S PROGRESS BODY (spec 2026-09-23 §2.4): unlinked orchestrations exactly as before siblings, an
/// endeavour as one block under a summed bar. The first case is the golden: its literal was derived from the
/// UNMOVED engine (<c>ProgressInGeneralTests</c>, green before the move), so it pins byte-identity.
/// <para>
/// Sessions are passed ORDERED BY ID rather than straight from <c>Load_All</c>, which enumerates folders and
/// is alphabetical on NTFS only: the builder follows its caller's order, and these tests pin that order.
/// </para>
/// </summary>
public class ProgressReportBuilderTests : IDisposable
{
    readonly EndeavourTree _tree = new();

    public void Dispose()
    {
        _tree.Dispose();
    }

    [Fact]
    public void Build_TwoUnlinkedOrchestrations_IsTodaysTextByteForByte()
    {
        _tree.Add_Solo("one", "one", endeavourId: null);
        _tree.Add_Solo("two", "two", endeavourId: null);
        Write_Plan("one", "- [x] a\n- [>] b\n- [ ] c\n");

        Assert.Equal(
            "one: 1/3 done (33%) · 1 running\ntwo: no task ledger yet",
            ProgressReport_Builder.Build_OpenOrchestrationsText(_tree.Paths, Sessions()));
    }

    [Fact]
    public void Build_NothingOpen_SaysSo()
    {
        _tree.Add_Solo("one", "one", endeavourId: null);
        _tree.Store.Close_Orchestration("one");

        Assert.Equal("no open orchestrations", ProgressReport_Builder.Build_OpenOrchestrationsText(_tree.Paths, Sessions()));
    }

    [Fact]
    public void TwoLinkedOpenSiblings_RenderAsOneGroup_WithTheSummedBar()
    {
        Add_Settings_And_Limits();

        Assert.Equal(
            "🔗 AI-Orch · settings work + limits work — 9/14 done (64%) · 2 running\n"
            + "   · AI-Orch · settings work: 6/8 done (75%) · 1 running\n"
            + "   · AI-Orch · limits work: 3/6 done (50%) · 1 running",
            ProgressReport_Builder.Build_OpenOrchestrationsText(_tree.Paths, Sessions()));
    }

    [Fact]
    public void AnUnlinkedOrchestrationBesideAGroup_RendersAsToday()
    {
        Add_Settings_And_Limits();
        _tree.Add_Solo("c-away", "AI-Orch · away mode loop", endeavourId: null);
        Write_Plan("c-away", Ledger(done: 4, running: 0, open: 0));

        var lines = ProgressReport_Builder.Build_OpenOrchestrationsText(_tree.Paths, Sessions()).Split('\n');

        Assert.Equal(4, lines.Length);
        Assert.StartsWith("🔗 ", lines[0]);
        Assert.Equal("AI-Orch · away mode loop: 4/4 done (100%)", lines[3]);
    }

    [Fact]
    public void TheGroup_RendersAtItsFirstMembersPosition()
    {
        _tree.Add_Solo("a-first", "AI-Orch · first job", endeavourId: null);
        _tree.Add_Solo("b-linked", "AI-Orch · linked one", "e-1");
        _tree.Add_Solo("c-middle", "AI-Orch · middle job", endeavourId: null);
        _tree.Add_Solo("d-linked", "AI-Orch · linked two", "e-1");

        var lines = ProgressReport_Builder.Build_OpenOrchestrationsText(_tree.Paths, Sessions()).Split('\n');

        Assert.Equal(
            [
                "AI-Orch · first job: no task ledger yet",
                "🔗 AI-Orch · linked one + linked two — no task ledger yet",
                "   · AI-Orch · linked one: no task ledger yet",
                "   · AI-Orch · linked two: no task ledger yet",
                "AI-Orch · middle job: no task ledger yet",
            ],
            lines);
    }

    [Fact]
    public void MixedPlatformCodes_JoinFullNames()
    {
        _tree.Add_Solo("a-sl", "SL · optimiser rework", "e-1");
        _tree.Add_Solo("b-is", "IS · portfolio picker", "e-1");

        var header = ProgressReport_Builder.Build_OpenOrchestrationsText(_tree.Paths, Sessions()).Split('\n')[0];

        Assert.Equal("🔗 SL · optimiser rework + IS · portfolio picker — no task ledger yet", header);
    }

    [Fact]
    public void ANameWithNoPlatformCode_JoinsFullNames()
    {
        _tree.Add_Solo("a-one", "AI-Orch · settings work", "e-1");
        _tree.Add_Solo("b-two", "limits", "e-1");

        var header = ProgressReport_Builder.Build_OpenOrchestrationsText(_tree.Paths, Sessions()).Split('\n')[0];

        Assert.Equal("🔗 AI-Orch · settings work + limits — no task ledger yet", header);
    }

    /// <summary>Review Focus 4. The endeavour's first orchestration closed; its two children live.</summary>
    [Fact]
    public void AClosedFirstMember_StaysInTheSumAndTheGroupName_ButGetsNoLine()
    {
        _tree.Add_Solo("a-parent", "AI-Orch · parent job", "e-1");
        _tree.Add_Solo("b-child", "AI-Orch · child one", "e-1");
        _tree.Add_Solo("c-child", "AI-Orch · child two", "e-1");
        Write_Plan("a-parent", Ledger(done: 5, running: 0, open: 0));
        Write_Plan("b-child", Ledger(done: 1, running: 1, open: 0));
        Write_Plan("c-child", Ledger(done: 0, running: 0, open: 2));
        _tree.Store.Close_Orchestration("a-parent");

        Assert.Equal(
            "🔗 AI-Orch · parent job + child one + child two — 6/9 done (66%) · 1 running\n"
            + "   · AI-Orch · child one: 1/2 done (50%) · 1 running\n"
            + "   · AI-Orch · child two: 0/2 done (0%)",
            ProgressReport_Builder.Build_OpenOrchestrationsText(_tree.Paths, Sessions()));
    }

    [Fact]
    public void AnEndeavourWithNoOpenMember_DoesNotRender()
    {
        _tree.Add_Solo("a-one", "AI-Orch · settings work", "e-1");
        _tree.Add_Solo("b-two", "AI-Orch · limits work", "e-1");
        _tree.Add_Solo("c-away", "AI-Orch · away mode loop", endeavourId: null);
        _tree.Store.Close_Orchestration("a-one");
        _tree.Store.Close_Orchestration("b-two");

        Assert.Equal(
            "AI-Orch · away mode loop: no task ledger yet",
            ProgressReport_Builder.Build_OpenOrchestrationsText(_tree.Paths, Sessions()));
        Assert.Null(ProgressReport_Builder.Build_EndeavourBlock_OrNull(_tree.Paths, Sessions(), "e-1"));
    }

    [Fact]
    public void TheEndeavourBlock_IsTheGroupGeneralPrints()
    {
        Add_Settings_And_Limits();
        _tree.Add_Solo("c-away", "AI-Orch · away mode loop", endeavourId: null);

        var block = ProgressReport_Builder.Build_EndeavourBlock_OrNull(_tree.Paths, Sessions(), "e-1");

        Assert.NotNull(block);
        Assert.StartsWith(block + "\n", ProgressReport_Builder.Build_OpenOrchestrationsText(_tree.Paths, Sessions()) + "\n");
        Assert.DoesNotContain("away mode loop", block);
    }

    [Fact]
    public void Build_CountsLine_CarriesTheDelta_WhenABaselineIsGiven()
    {
        _tree.Add_Solo("one", "one", endeavourId: null);
        Write_Plan("one", Ledger(done: 2, running: 0, open: 1));

        Assert.Equal(
            "one: 2(+1)/3(+1) done (66% +16%)",
            ProgressReport_Builder.Build_CountsLine(_tree.Paths, "one", "one", new AIOrchestratorCoreLib.Planning.PlanProgressSnapshot(1, 2)));
    }

    void Add_Settings_And_Limits()
    {
        _tree.Add_Solo("a-settings", "AI-Orch · settings work", "e-1");
        _tree.Add_Solo("b-limits", "AI-Orch · limits work", "e-1");
        Write_Plan("a-settings", Ledger(done: 6, running: 1, open: 1));
        Write_Plan("b-limits", Ledger(done: 3, running: 1, open: 2));
    }

    IReadOnlyList<IOrchestrationSession> Sessions()
    {
        return [.. _tree.Sessions().OrderBy(session => session.OrchId, StringComparer.Ordinal)];
    }

    void Write_Plan(string orchId, string text)
    {
        File.WriteAllText(_tree.Paths.Get_PlanFile(orchId), text);
    }

    internal static string Ledger(int done, int running, int open)
    {
        return string.Concat(
            Enumerable.Range(0, done).Select(i => $"- [x] done {i}\n")
                .Concat(Enumerable.Range(0, running).Select(i => $"- [>] running {i}\n"))
                .Concat(Enumerable.Range(0, open).Select(i => $"- [ ] open {i}\n")));
    }
}
