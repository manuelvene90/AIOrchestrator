using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Tests.Running;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// WHOSE turn the owner is waiting on is resolved, never spelled: a basic orchestration has no
/// supervisor, and reading the supervisor's state file there would answer "nothing failed" for a solo
/// that cannot answer — the promise this reader exists to withhold (2026-09-11).
/// </summary>
public class OwnerFacingTurnReaderTests
{
    const string NOT_LOGGED_IN = """{"default":{"is_error":true,"exit_code":1,"result":"Not logged in · Please run /login"}}""";

    [Fact]
    public async Task ASolosFailedTurn_IsReadFromTheSolo_NotFromAnAbsentSupervisor()
    {
        using var harness = new PrintRunnerTestHarness("solo");
        var (orchId, soloId) = harness.Register_Member(MemberKinds.Solo);
        harness.Write_Scenario(NOT_LOGGED_IN);

        // The solo's boot turn is its first turn and needs nothing said to it; it fails like any other.
        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Solo, orchId, soloId).FailedAttempts >= 1, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var read = OwnerFacingTurn_Reader.Read_CurrentTurnFailures(harness.Paths, orchId, harness.Store.Get_Session_OrNull(orchId));

        Assert.Null(read.ReadFailure);
        Assert.True(read.FailedAttempts >= 1, $"the solo's failed turn was not seen (read {read.FailedAttempts})");
    }

    /// <summary>
    /// DECISION 21: a predicate that cannot be evaluated says so and allows. A corrupt state file is
    /// zero failures WITH a reason, so the bridge keeps its old receipt and logs why — it does not
    /// invent a failure the file does not show.
    /// </summary>
    [Fact]
    public void AnUnreadableStateFile_IsZeroWithAReason()
    {
        using var harness = new PrintRunnerTestHarness("supervisor");
        var orchId = "repo-1";
        harness.Register_Supervisor(orchId, SessionRunners.Print);

        File.WriteAllText(PrintSessionState_Store.Get_StateFile(harness.Paths, SessionRoles.Supervisor, orchId, "sup"), "{ not json");

        var read = OwnerFacingTurn_Reader.Read_CurrentTurnFailures(harness.Paths, orchId, harness.Store.Get_Session_OrNull(orchId));

        Assert.Equal(0, read.FailedAttempts);
        Assert.NotNull(read.ReadFailure);
    }

    [Fact]
    public void ATerminalRunSession_HasNoStateFile_AndNothingFailed()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        harness.Store.Create_Orchestration("repo-1", "Repo", harness.RepoPath);

        var read = OwnerFacingTurn_Reader.Read_CurrentTurnFailures(harness.Paths, "repo-1", harness.Store.Get_Session_OrNull("repo-1"));

        Assert.Equal(0, read.FailedAttempts);
        Assert.Null(read.ReadFailure);
    }
}
