using System.Diagnostics;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintTurnDispatcher;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.Sessions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE OWNER'S SHORT WINDOW AT ITS CALL SITE — that the dispatcher actually asks
/// <c>CoalesceWindow_Policy.Resolve_Window</c> before it decides how long to wait, that member traffic
/// still serves the window it was written for, and that "short" is not "none".
///
/// <para>
/// The policy's own tests are pure and say nothing about whether anybody consults it: delete the call
/// in <c>Consider_Session</c> and they all stay green. These are the ones that redden — the owner does
/// not wait, a member does, and something landing beside the owner still rides their turn — driven
/// through the real dispatcher against the FakeClaude stub.
/// </para>
/// <para>
/// FIVE SECONDS OF WINDOW, measured against a stopwatch, because the subject IS a duration. The
/// numbers are deliberately far apart: the owner's turn must start in under two and a half seconds
/// (it should start on the first pass, ~0) and the member's must not start before four and a half
/// (its window is five). A machine under load can only make the first assertion fail by an order of
/// magnitude, which is a real failure rather than a race — the shape MultiSourceSupervisorTests
/// documents from the 2026-09-06 load-42 run.
/// </para>
/// <para>
/// TURN START, not turn end: <see cref="IPrintTurnDispatcher.Is_TurnInFlight"/> is true the moment the
/// dispatcher stops waiting, so neither number carries the cost of spawning a process.
/// </para>
/// </summary>
public class OwnerTrafficSkipsTheCoalesceWindowTests
{
    /// <summary>The window under test. Long enough that "waited" and "did not wait" cannot be confused.</summary>
    const double COALESCE_SECONDS = 5;

    /// <summary>
    /// Measured on the VPS on 2026-09-09: 11–12 s median from the owner's Telegram message to their
    /// supervisor's turn starting, three of them this window. The owner's ruling was that their own
    /// message does not serve it (the aggregation window and the mirror tick are the other two thirds,
    /// each addressed in its own place).
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AnOwnerMessage_StartsTheTurnWithoutServingTheWindow()
    {
        using var harness = new PrintRunnerTestHarness("supervisor", coalesceSeconds: COALESCE_SECONDS);
        harness.Register_Supervisor("repo-1", SessionRunners.Print);
        harness.Write_Scenario("""{"default":{"result":"on it"}}""");

        var dispatcher = harness.Create_Dispatcher();

        Assert.True(ChannelAppender.Append_OwnerEntry(harness.Paths.Get_OwnerChannelFile("repo-1"), "restart the crew.", DateTime.Now));

        var stopwatch = Stopwatch.StartNew();

        Assert.True(
            PrintRunnerTestHarness.Drive_Until(dispatcher, () => Turn_InFlight(dispatcher), PrintRunnerTestHarness.GENEROUS),
            "the owner's message never started a turn at all.");

        var waited = stopwatch.Elapsed;

        await dispatcher.Stop_Async();

        Assert.True(
            waited < TimeSpan.FromSeconds(2.5),
            $"the owner served the coalesce window: their turn started after {waited.TotalSeconds:F2} s of a {COALESCE_SECONDS:F0} s window.");
    }

    /// <summary>
    /// THE CONTROL, and the expensive half of the rule. An extra supervisor wake costs ~1 M input
    /// tokens (measured), so a crew filing reports within a second of each other must still be read in
    /// ONE turn — the waiver is the owner's alone, and a change that dropped the window for everybody
    /// would pass the test above and reduce this one to a per-report bill.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AMemberReport_StillServesTheWholeWindow()
    {
        using var harness = new PrintRunnerTestHarness("supervisor", coalesceSeconds: COALESCE_SECONDS);
        harness.Register_Supervisor("repo-1", SessionRunners.Print);
        harness.Write_Scenario("""{"default":{"result":"noted"}}""");

        var member = harness.Store.Add_Member("repo-1", MemberKinds.Implementer).Members[^1].MemberId;
        var dispatcher = harness.Create_Dispatcher();

        Assert.True(ChannelAppender.Append_SessionEntry(
            harness.Paths.Get_ImplementerChannelFile("repo-1", member), ChannelAuthors.Implementer, "REPORT — parser", "Parser done.", DateTime.Now));

        var stopwatch = Stopwatch.StartNew();

        Assert.True(
            PrintRunnerTestHarness.Drive_Until(dispatcher, () => Turn_InFlight(dispatcher), PrintRunnerTestHarness.GENEROUS),
            "the member's report never started a turn at all.");

        var waited = stopwatch.Elapsed;

        await dispatcher.Stop_Async();

        Assert.True(
            waited >= TimeSpan.FromSeconds(4.5),
            $"a member's report skipped the coalesce window: the turn started after {waited.TotalSeconds:F2} s of a {COALESCE_SECONDS:F0} s window.");
    }

    /// <summary>
    /// AND THE OWNER'S HASTE MUST NOT COST A WHOLE EXTRA TURN — the half the waiver got wrong.
    ///
    /// <para>
    /// The waiver was written as "skip the WAIT, never narrow the turn", and the second half was not
    /// true: a turn that has already STARTED cannot take anything else, so a member report landing a
    /// breath after the owner's message hits <c>_inFlight</c>, waits for that turn to end, and buys its
    /// own. Driven through the real dispatcher on the merged code: an owner entry followed by a member
    /// entry one second later produced TWO turns, where two member entries a second apart produced one.
    /// The exposure was the whole coalesce window after every owner message, and an extra supervisor
    /// wake is ~1 M input tokens, measured.
    /// </para>
    /// <para>
    /// So the owner's traffic no longer waives the window — it serves a SHORT one
    /// (<c>CoalesceWindow_Policy.OWNER_BREATH_MILLISECONDS</c>), which is what lets whatever was already
    /// on its way ride along while keeping the owner off the three seconds they asked to be rid of. The
    /// pause below is a quarter of a second: far inside that breath, and far outside the milliseconds
    /// the merged code took to start the turn.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AMemberReportLandingBesideAnOwnerMessage_RidesTheSameTurn()
    {
        using var harness = new PrintRunnerTestHarness("supervisor", coalesceSeconds: COALESCE_SECONDS);
        harness.Register_Supervisor("repo-1", SessionRunners.Print);
        harness.Write_Scenario("""{"default":{"result":"on it"}}""");

        var member = harness.Store.Add_Member("repo-1", MemberKinds.Implementer).Members[^1].MemberId;
        var dispatcher = harness.Create_Dispatcher();

        Assert.True(ChannelAppender.Append_OwnerEntry(harness.Paths.Get_OwnerChannelFile("repo-1"), "restart the crew.", DateTime.Now));

        dispatcher.Tick(DateTime.Now);
        Thread.Sleep(250);

        Assert.True(ChannelAppender.Append_SessionEntry(
            harness.Paths.Get_ImplementerChannelFile("repo-1", member), ChannelAuthors.Implementer, "REPORT — parser", "Parser done.", DateTime.Now));

        Assert.True(
            PrintRunnerTestHarness.Drive_Until(
                dispatcher,
                () => harness.Read_State(SessionRoles.Supervisor, "repo-1", SessionLaunch_Factory.SUPERVISOR_MEMBER_ID).ExecutedTurns.Count > 0,
                PrintRunnerTestHarness.GENEROUS),
            "the supervisor never took a turn at all.");

        // Past the whole window from the LATER entry, so a second turn would have had every chance to
        // start: the assertion below is "there was no second turn", not "there was not one yet".
        PrintRunnerTestHarness.Drive_Until(dispatcher, () => false, TimeSpan.FromSeconds(COALESCE_SECONDS + 2));

        await dispatcher.Stop_Async();

        var invocations = harness.Read_Invocations();

        Assert.True(
            invocations.Count == 1,
            $"the owner's message and the report a quarter of a second behind it bought {invocations.Count} supervisor turns.");

        // AND THE ONE TURN READ BOTH OF THEM. Without this the assertion above would also be satisfied
        // by a turn that started late and left the report pending — which is a different bug wearing the
        // same number. A first turn hands nothing over in its prompt (the session is spawned with its
        // role command and reads its own channels), so what says the entries were handed over is the
        // cursor the turn advanced.
        var state = harness.Read_State(SessionRoles.Supervisor, "repo-1", SessionLaunch_Factory.SUPERVISOR_MEMBER_ID);

        Assert.Single(state.ExecutedTurns);
        Assert.Equal(2, state.Cursors.Count(cursor => cursor.Delivered.Count > 0));
    }

    static bool Turn_InFlight(IPrintTurnDispatcher dispatcher)
    {
        return dispatcher.Is_TurnInFlight("repo-1", SessionLaunch_Factory.SUPERVISOR_MEMBER_ID);
    }
}
