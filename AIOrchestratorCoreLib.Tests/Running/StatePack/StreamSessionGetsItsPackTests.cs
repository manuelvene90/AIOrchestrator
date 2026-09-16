using System.IO;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.Running.StatePack;
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

    /// <summary>
    /// THE ENTRIES TRAVEL ONCE. With the pack written (the case above) and the follow-up prompt
    /// unchanged, a fresh stream turn paid for its pending entries TWICE in the same turn — once in
    /// the file and once on stdin. On the supervisor, whose entries are the expensive part, that is
    /// the saving handed straight back. The print transport never had this problem: a fresh print
    /// turn sends nothing on stdin at all. A stream process must be sent something, so it is sent the
    /// shortest thing that still carries the one item stdin is needed for — the bridge-turn marker,
    /// which the entry splitter and the per-stage accounting read.
    /// </summary>
    [Fact]
    public async Task A_fresh_stream_turn_is_pointed_at_its_pack_instead_of_being_sent_its_entries()
    {
        using var harness = new PrintRunnerTestHarness("supervisor:stream", resumeForMembers: "fresh");
        var memberId = Supervisor(harness);
        harness.Write_Scenario("""{"turns":[{"result":"sup online"},{"result":"VERDICT — merged\n\nDone."}]}""");
        var dispatcher = harness.Create_Dispatcher();

        Assert.True(ChannelAppender.Append_OwnerEntry(harness.Paths.Get_OwnerChannelFile(ORCH), "do the merge", DateTime.Now));
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Turned(harness, 1), PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var turnMessage = Assert.Single(Messages(harness), prompt => prompt.StartsWith("[bridge turn ", StringComparison.Ordinal));

        Assert.Contains(StatePack_Locator.SUPERVISOR_FILE_NAME, turnMessage);
        Assert.DoesNotContain("do the merge", turnMessage);

        // …which is in the pack, where it is paid for once.
        Assert.Contains("do the merge", harness.Read_Pack_OrNull(SessionRoles.Supervisor, ORCH, memberId)!);
    }

    /// <summary>
    /// A RESUMED TURN IS UNCHANGED — its entries still ride stdin, because it has no pack and its
    /// transcript is what it reads them against. The pointer must not reach it.
    /// </summary>
    [Fact]
    public async Task A_resumed_stream_turn_still_carries_its_entries()
    {
        using var harness = new PrintRunnerTestHarness("supervisor:stream", resumeForMembers: "transcript");
        Supervisor(harness);
        harness.Write_Scenario("""{"turns":[{"result":"sup online"},{"result":"VERDICT — merged\n\nDone."}]}""");
        var dispatcher = harness.Create_Dispatcher();

        Assert.True(ChannelAppender.Append_OwnerEntry(harness.Paths.Get_OwnerChannelFile(ORCH), "do the merge", DateTime.Now));
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Turned(harness, 1), PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var turnMessage = Assert.Single(Messages(harness), prompt => prompt.StartsWith("[bridge turn ", StringComparison.Ordinal));

        Assert.Contains("do the merge", turnMessage);
        Assert.DoesNotContain(StatePack_Locator.SUPERVISOR_FILE_NAME, turnMessage);
    }

    /// <summary>Every message written on a stream session's stdin, in order.</summary>
    static IReadOnlyList<string> Messages(PrintRunnerTestHarness harness)
    {
        return
        [
            .. harness.Read_Invocations()
                .Where(line => line["line_kind"]?.GetValue<string>() == "stream-message")
                .Select(line => line["prompt"]!.GetValue<string>())
        ];
    }
}
