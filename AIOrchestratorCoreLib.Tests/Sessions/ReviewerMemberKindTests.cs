using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.Spawning;
using AIOrchestratorCoreLib.Spawning.SessionSpawner;
using AIOrchestratorCoreLib.Spawning.SpawnCommand;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Sessions;

/// <summary>
/// Guards the reviewer kind. The load-bearing property is that the KIND lives in the member id, so
/// every path that only has an id (respawn, the watchdog, the UI) still knows a reviewer is
/// read-only. The dangerous failure is a reviewer coming back from a respawn as a writable
/// implementer — that is what these tests exist to catch.
/// </summary>
public class ReviewerMemberKindTests : IDisposable
{
    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly RecordingSpawner_Fake _spawner;
    readonly IOrchestrationLauncher _launcher;

    public ReviewerMemberKindTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-reviewer-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _spawner = new RecordingSpawner_Fake();

        _launcher = OrchestrationLauncher_Factory.Create(
            _paths,
            OrchestratorConfigProvider_Factory.Create(_paths),
            _store,
            _spawner,
            OrchestrationLog_Factory.Create(_paths));
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// Owner directive: nobody reviews their own work, not even for a small task. If the reviewer
    /// had to be requested first, the cheap path would be self-review — so rev-1 exists before the
    /// first task does, and it comes up read-only like any other reviewer.
    /// </summary>
    [Fact]
    public void Every_Orchestration_StartsWithAReviewer_AlreadyReadOnly()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);

        Assert.Equal(["imp-1", "rev-1"], session.Members.Select(m => m.MemberId));

        var reviewerScript = _spawner.SpawnedCommands
            .Select(SpawnCommand_Builder.Decode_SessionScript)
            .Single(script => script.Contains("/reviewer "));

        Assert.Contains("--disallowedTools \"Write\" \"Edit\" \"NotebookEdit\"", reviewerScript);
        Assert.Contains($"/reviewer {session.OrchId}/rev-1", reviewerScript);
    }

    [Theory]
    [InlineData("imp-1", MemberKinds.Implementer)]
    [InlineData("imp-12", MemberKinds.Implementer)]
    [InlineData("rev-1", MemberKinds.Reviewer)]
    [InlineData("REV-3", MemberKinds.Reviewer)]
    public void Resolve_Kind_ReadsTheKindOutOfTheId(string memberId, MemberKinds expected)
    {
        Assert.Equal(expected, MemberKind_Ids.Resolve_Kind(memberId));
    }

    [Fact]
    public void Kinds_NumberIndependently_SoIdsReadAsImp1Imp2Rev1()
    {
        _store.Create_Orchestration("orch-1", "Repo", _tempRepo);

        _store.Add_Member("orch-1", MemberKinds.Implementer);
        _store.Add_Member("orch-1", MemberKinds.Reviewer);
        var session = _store.Add_Member("orch-1", MemberKinds.Implementer);

        Assert.Equal(["imp-1", "rev-1", "imp-2"], session.Members.Select(m => m.MemberId));
    }

    [Fact]
    public void Reviewer_Spawn_WithholdsTheEditingTools_AndRunsTheReviewerCommand()
    {
        var command = SpawnCommand_Builder.Build_ForReviewer("orch-1", "rev-1", _tempRepo, null, "pid.txt", null);
        var script = SpawnCommand_Builder.Decode_SessionScript(command);

        Assert.Contains("--disallowedTools \"Write\" \"Edit\" \"NotebookEdit\"", script);
        Assert.Contains("/reviewer orch-1/rev-1", script);
        Assert.Contains("AIORCH_ROLE='reviewer'", script);

        // The prompt MUST be terminated off the variadic --disallowedTools, or the CLI parses it as
        // tool names and the session starts blank. That bug respawned reviewers in a loop.
        Assert.Contains("-- '/reviewer orch-1/rev-1'", script);
        Assert.True(
            script.IndexOf("--disallowedTools", StringComparison.Ordinal) < script.IndexOf("-- '/reviewer", StringComparison.Ordinal),
            "the '--' terminator must come AFTER the variadic flag it is protecting the prompt from");
    }

    [Fact]
    public void Implementer_Spawn_KeepsTheEditingTools()
    {
        var command = SpawnCommand_Builder.Build_ForImplementer("orch-1", "imp-1", _tempRepo, null, "pid.txt", null);
        var script = SpawnCommand_Builder.Decode_SessionScript(command);

        Assert.DoesNotContain("--disallowedTools", script);
        Assert.Contains("/implementer orch-1/imp-1", script);
    }

    [Fact]
    public void Add_Member_Reviewer_SpawnsAReadOnlySession()
    {
        var started = _launcher.Start_Orchestration("Repo", _tempRepo);
        _spawner.SpawnedCommands.Clear();

        var session = _launcher.Add_Member(started.OrchId, MemberKinds.Reviewer);

        // rev-1 is the one every orchestration starts with, so an explicitly added one is rev-2.
        Assert.Equal("rev-2", session.Members[session.Members.Count - 1].MemberId);

        var script = SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands.Single());
        Assert.Contains("--disallowedTools", script);
    }

    /// <summary>
    /// The watchdog respawns from a member id alone. If that path forgot the kind, a crashed
    /// reviewer would silently come back able to edit and commit — the whole guarantee gone, with
    /// nothing in the UI to show it.
    /// </summary>
    [Fact]
    public void Respawn_OfAReviewer_ComesBackReadOnly()
    {
        var started = _launcher.Start_Orchestration("Repo", _tempRepo);
        _spawner.SpawnedCommands.Clear();

        _launcher.Respawn_Implementer(started.OrchId, "rev-1");

        var script = SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands.Single());
        Assert.Contains("--disallowedTools \"Write\" \"Edit\" \"NotebookEdit\"", script);
        Assert.Contains($"/reviewer {started.OrchId}/rev-1", script);
    }

    [Fact]
    public void Respawn_OfAnImplementer_IsUnaffected()
    {
        var started = _launcher.Start_Orchestration("Repo", _tempRepo);
        _spawner.SpawnedCommands.Clear();

        _launcher.Respawn_Implementer(started.OrchId, "imp-1");

        var script = SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands.Single());
        Assert.DoesNotContain("--disallowedTools", script);
        Assert.Contains($"/implementer {started.OrchId}/imp-1", script);
    }
}
