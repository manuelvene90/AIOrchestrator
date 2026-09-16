using System.IO;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running.StatePack;

/// <summary>
/// THE SUPERVISOR'S OWN TRANSPORT HAD NO PACK. Verified 2026-09-15 on `feat/supervisor-fresh` at
/// c3b53fc: <c>StreamTurnExecutorModel</c> contained ZERO references to StatePack, while
/// <c>PrintTurnExecutorModel</c> wrote one from a private method. The supervisor runs on stream by
/// design (p50 1.27 s against print's 5.77 s), so a supervisor flipped to <c>resume: fresh</c> would
/// have got its role command and its pending entries and nothing else — no brief, no ledger, no git
/// state, no owner tail, no conclusions. That is the amnesia plan 06 exists to prevent, and it was
/// one config word away.
/// </summary>
public class StreamSessionGetsItsPackTests
{
    const string ORCH = "repo-1";

    static string Supervisor(PrintRunnerTestHarness harness, SessionRunners runner = SessionRunners.Stream)
    {
        return harness.Register_Supervisor(ORCH, runner);
    }

    static bool Turned(PrintRunnerTestHarness harness, int turns)
    {
        return harness.Read_State(SessionRoles.Supervisor, ORCH, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID).ExecutedTurns.Count == turns;
    }

    [Fact]
    public async Task A_fresh_stream_supervisor_is_handed_a_pack()
    {
        using var harness = new PrintRunnerTestHarness("supervisor:stream", resumeForMembers: "fresh");
        var memberId = Supervisor(harness);
        File.WriteAllText(harness.Paths.Get_PlanFile(ORCH), "# PLAN\n\n- [ ] merge the branch (imp-1)\n");
        harness.Write_Scenario("""{"turns":[{"result":"sup online"},{"result":"VERDICT — merged\n\nDone."}]}""");
        var dispatcher = harness.Create_Dispatcher();

        Assert.True(ChannelAppender.Append_OwnerEntry(harness.Paths.Get_OwnerChannelFile(ORCH), "do the merge", DateTime.Now));
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Turned(harness, 1), PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var pack = harness.Read_Pack_OrNull(SessionRoles.Supervisor, ORCH, memberId);

        Assert.NotNull(pack);
        Assert.Contains("do the merge", pack);

        // The supervisor's own sections — it owns the endeavour, so it gets the whole ledger.
        // THE SUPERVISOR OWNS THE ENDEAVOUR, so it gets the WHOLE ledger rather than the lines that
        // name it — the `ownsTheEndeavour` branch of StatePackInputs_Reader, which nothing had ever
        // exercised through this transport.
        Assert.Contains("## The ledger — PLAN.md", pack!);
        Assert.Contains("- [ ] merge the branch (imp-1)", pack);
    }

    /// <summary>
    /// AND A RESUMED ONE IS NOT. A pack is what REPLACES a transcript; a session that still has its
    /// transcript is handed its entries on stdin as it always was, and writing a pack for it would put
    /// the same context in the same turn twice.
    /// </summary>
    [Fact]
    public async Task A_resumed_stream_supervisor_gets_no_pack()
    {
        using var harness = new PrintRunnerTestHarness("supervisor:stream", resumeForMembers: "transcript");
        var memberId = Supervisor(harness);
        harness.Write_Scenario("""{"turns":[{"result":"sup online"},{"result":"VERDICT — merged\n\nDone."}]}""");
        var dispatcher = harness.Create_Dispatcher();

        Assert.True(ChannelAppender.Append_OwnerEntry(harness.Paths.Get_OwnerChannelFile(ORCH), "do the merge", DateTime.Now));
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Turned(harness, 1), PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        Assert.Null(harness.Read_Pack_OrNull(SessionRoles.Supervisor, ORCH, memberId));
    }
}
