using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.Spawning;
using AIOrchestratorCoreLib.Spawning.SpawnCommand;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.TestSupport;
using AIOrchestratorCoreLib.Usage;
using AIOrchestratorCoreLib.Watchdog.SessionWatchdog;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Launching;

/// <summary>
/// A SIBLING RUNS IN ITS OWN TREE, AND KEEPS RUNNING THERE (spec §4.3 step 3, §6, §7.1).
///
/// <para>
/// Every assertion here is on what the SPAWNER WAS HANDED — the command a real <c>wt.exe</c> would
/// have run — never on session.json afterwards. The store's opinion of a field is not what the first
/// process saw: the window title and the working directory are read at spawn, so a field stamped one
/// line after <c>Add_Member</c> is a field that is right on disk and wrong in the only process that
/// matters.
/// </para>
/// <para>
/// THE RESPAWN ROUTES ARE TESTED ONE BY ONE because resume depends on them. Claude Code keeps its
/// transcripts per working directory, so a sibling respawned in the repo root instead of its worktree
/// would not fail — it would silently start a FRESH conversation (the resolver only checks that the
/// transcript file exists) while the log still says it was respawned. The watchdog, <c>/model</c>,
/// <c>/effort</c> and an app restart all go through <c>Respawn_Implementer</c>; each is driven here as
/// production drives it.
/// </para>
/// </summary>
public class SiblingLaunchTests : IDisposable
{
    const string CHILD_NAME = "AI-Orch · limits rework";
    const string SOLO_SESSION_ID = "66666666-7777-4888-8999-aaaaaaaaaaaa";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly string _worktree;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly RecordingSpawner_Fake _spawner;
    readonly IOrchestrationLauncher _launcher;

    public SiblingLaunchTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-sibling-launch-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        // A PLAIN DIRECTORY, not a git worktree: the launcher checks only that the folder exists. Whether
        // it is a worktree of this repo is the validator's question (Task 6), asked before this call.
        _worktree = Path.Combine(_tempRoot, "repo.worktrees", "limits");
        Directory.CreateDirectory(_worktree);

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
        TempTree.Delete_BestEffort(_tempRoot);
    }

    /// <summary>THE FIRST PROCESS SEES THE LINK. Asserted on what the spawner was HANDED, not on session.json afterwards.</summary>
    [Fact]
    public void TheChildIsSpawnedInItsWorktree_WithItsNameOnTheWindow()
    {
        var parent = _launcher.Start_BasicOrchestration("AIOrchestrator", _tempRepo);
        _spawner.SpawnedCommands.Clear();

        var child = _launcher.Start_SiblingOrchestration(parent.OrchId, CHILD_NAME, _worktree, $"{parent.OrchId}#14");

        var spawn = Assert.Single(_spawner.SpawnedCommands);

        Assert.Equal(_worktree, spawn.WorkingDirectory);
        Assert.Equal(_worktree, RecordingSpawner_Fake.Read_Argument_After(spawn, "-d"));
        Assert.Equal(
            SessionWindowTitle_Builder.Build_Title(SessionWindowTitle_Builder.Build_ForMember("solo-1", child.OrchId), CHILD_NAME),
            RecordingSpawner_Fake.Read_Argument_After(spawn, "--title"));
        Assert.Contains($"'/solo {child.OrchId}'", SpawnCommand_Builder.Decode_SessionScript(spawn));
        Assert.NotEqual(parent.OrchId, child.OrchId);
    }

    /// <summary>
    /// The four link fields, and the parent's two dials — the owner's setting on this endeavour, which
    /// half of it must not quietly drop (spec §4.1). The dials are checked on the FIRST command line too,
    /// because a copy stamped after the spawn would be on disk and absent from the session.
    /// </summary>
    [Fact]
    public void TheChildCarriesTheLink_AndTheParentsDials()
    {
        var parent = _launcher.Start_BasicOrchestration("AIOrchestrator", _tempRepo);
        _store.Set_ImplementerModelOverride(parent.OrchId, "sonnet");
        _store.Set_ImplementerEffortOverride(parent.OrchId, "low");
        _spawner.SpawnedCommands.Clear();

        var child = _launcher.Start_SiblingOrchestration(parent.OrchId, CHILD_NAME, _worktree, $"{parent.OrchId}#14");
        var stored = _store.Get_Session(child.OrchId);

        Assert.Equal(parent.OrchId, stored.EndeavourId);
        Assert.Equal(parent.OrchId, stored.BornFromOrchId);
        Assert.Equal($"{parent.OrchId}#14", stored.BornFromHandover);
        Assert.Equal(_worktree, stored.WorkingPath);
        Assert.Equal(CHILD_NAME, stored.DisplayName);
        Assert.Equal("sonnet", stored.ImplementerModelOverride);
        Assert.Equal("low", stored.ImplementerEffortOverride);
        Assert.Equal(parent.RepoPath, stored.RepoPath);
        Assert.Equal(parent.RepoName, stored.RepoName);

        var script = SpawnCommand_Builder.Decode_SessionScript(Assert.Single(_spawner.SpawnedCommands));
        Assert.Contains("--model sonnet", script);
        Assert.Contains("--effort low", script);
    }

    /// <summary>
    /// A LAUNCHER DOES NOT EDIT OTHER ORCHESTRATIONS. Stamping the parent's own endeavour id is the
    /// birth step's job (Task 7); here the parent is read, never written.
    /// </summary>
    [Fact]
    public void TheParent_IsNotStamped_ByTheLauncher()
    {
        var parent = _launcher.Start_BasicOrchestration("AIOrchestrator", _tempRepo);

        _launcher.Start_SiblingOrchestration(parent.OrchId, CHILD_NAME, _worktree, $"{parent.OrchId}#14");

        Assert.Null(_store.Get_Session(parent.OrchId).EndeavourId);
    }

    /// <summary>
    /// THE ENDEAVOUR ID IS THE FIRST ORCHESTRATION'S, for ever (spec §3.2). A sibling of a sibling joins
    /// the same group — a child keyed on its own parent's id would start a second endeavour and split
    /// the bar, the digest and the cap in two.
    /// </summary>
    [Fact]
    public void AGrandchild_JoinsTheFirstEndeavour_NotItsParentsId()
    {
        var parent = _launcher.Start_BasicOrchestration("AIOrchestrator", _tempRepo);
        _store.Set_EndeavourId(parent.OrchId, "root-1");

        var child = _launcher.Start_SiblingOrchestration(parent.OrchId, CHILD_NAME, _worktree, $"{parent.OrchId}#14");
        var stored = _store.Get_Session(child.OrchId);

        Assert.Equal("root-1", stored.EndeavourId);
        Assert.Equal(parent.OrchId, stored.BornFromOrchId);
    }

    /// <summary>
    /// THE FOLDER IS CHECKED BEFORE THE ID IS SPENT. A missing tree after allocation would leave an
    /// orchestration on disk with a session that can never spawn — the watchdog would respawn it into
    /// the same missing folder for ever.
    /// </summary>
    [Fact]
    public void AMissingWorktree_Throws_AndCreatesNothing()
    {
        var parent = _launcher.Start_BasicOrchestration("AIOrchestrator", _tempRepo);
        var sessionsBefore = _store.Load_All().Count;
        _spawner.SpawnedCommands.Clear();

        Assert.ThrowsAny<Exception>(() => _launcher.Start_SiblingOrchestration(
            parent.OrchId, CHILD_NAME, Path.Combine(_tempRoot, "repo.worktrees", "gone"), $"{parent.OrchId}#14"));

        Assert.Equal(sessionsBefore, _store.Load_All().Count);
        Assert.Empty(_spawner.SpawnedCommands);
    }

    /// <summary>
    /// A RELATIVE TREE IS REFUSED, never resolved. `Directory.Exists("limits")` answers for the APP's
    /// current directory, which is nobody's worktree; stored, it would become the cwd of every respawn.
    /// </summary>
    [Fact]
    public void ARelativeWorktree_Throws_AndCreatesNothing()
    {
        var parent = _launcher.Start_BasicOrchestration("AIOrchestrator", _tempRepo);
        var sessionsBefore = _store.Load_All().Count;
        _spawner.SpawnedCommands.Clear();

        Assert.ThrowsAny<Exception>(() => _launcher.Start_SiblingOrchestration(
            parent.OrchId, CHILD_NAME, Path.Combine("repo.worktrees", "limits"), $"{parent.OrchId}#14"));

        Assert.Equal(sessionsBefore, _store.Load_All().Count);
        Assert.Empty(_spawner.SpawnedCommands);
    }

    /// <summary>
    /// THE WATCHDOG'S ROUTE, driven through the real watchdog. The member's spawn stamp is aged so the
    /// grace window does not skip it, which is what every session looks like once it has run a while.
    /// </summary>
    [Fact]
    public void AWatchdogRespawn_UsesTheWorktree()
    {
        var parent = _launcher.Start_BasicOrchestration("AIOrchestrator", _tempRepo);
        var child = _launcher.Start_SiblingOrchestration(parent.OrchId, CHILD_NAME, _worktree, $"{parent.OrchId}#14");
        Age_MemberSpawn(child.OrchId);
        _spawner.SpawnedCommands.Clear();

        var watchdog = SessionWatchdog_Factory.Create(
            _paths, OrchestratorConfigProvider_Factory.Create(_paths), _store, _launcher, OrchestrationLog_Factory.Create(_paths));
        watchdog.Check_AndRestart_DeadSessions();

        Assert.Equal(_worktree, Single_SoloSpawn(child.OrchId).WorkingDirectory);
    }

    /// <summary>
    /// AN APP RESTART: a new store, a new launcher and a new watchdog, built from the disk alone — so the
    /// tree has to come back from session.json, since nothing in memory survived.
    /// </summary>
    [Fact]
    public void AnAppRestartRespawn_UsesTheWorktree()
    {
        var parent = _launcher.Start_BasicOrchestration("AIOrchestrator", _tempRepo);
        var child = _launcher.Start_SiblingOrchestration(parent.OrchId, CHILD_NAME, _worktree, $"{parent.OrchId}#14");
        Age_MemberSpawn(child.OrchId);
        _spawner.SpawnedCommands.Clear();

        var restartedPaths = SupervisionPaths_Factory.Create(_tempRoot);
        var restartedStore = OrchestrationSessionStore_Factory.Create(restartedPaths);
        var restartedLog = OrchestrationLog_Factory.Create(restartedPaths);
        var restartedLauncher = OrchestrationLauncher_Factory.Create(
            restartedPaths, OrchestratorConfigProvider_Factory.Create(restartedPaths), restartedStore, _spawner, restartedLog);
        var restartedWatchdog = SessionWatchdog_Factory.Create(
            restartedPaths, OrchestratorConfigProvider_Factory.Create(restartedPaths), restartedStore, restartedLauncher, restartedLog);

        restartedWatchdog.Check_AndRestart_DeadSessions();

        Assert.Equal(_worktree, Single_SoloSpawn(child.OrchId).WorkingDirectory);
    }

    /// <summary>/model: the override is stored, then the member is respawned (the engine's Apply_Dial does exactly this).</summary>
    [Fact]
    public void AModelDialRespawn_UsesTheWorktree()
    {
        var parent = _launcher.Start_BasicOrchestration("AIOrchestrator", _tempRepo);
        var child = _launcher.Start_SiblingOrchestration(parent.OrchId, CHILD_NAME, _worktree, $"{parent.OrchId}#14");
        _store.Set_ImplementerModelOverride(child.OrchId, "haiku");
        _spawner.SpawnedCommands.Clear();

        _launcher.Respawn_Implementer(child.OrchId, "solo-1");

        var spawn = Single_SoloSpawn(child.OrchId);
        Assert.Equal(_worktree, spawn.WorkingDirectory);
        Assert.Contains("--model haiku", SpawnCommand_Builder.Decode_SessionScript(spawn));
    }

    /// <summary>/effort: the same apply path as /model, with the other dial.</summary>
    [Fact]
    public void AnEffortDialRespawn_UsesTheWorktree()
    {
        var parent = _launcher.Start_BasicOrchestration("AIOrchestrator", _tempRepo);
        var child = _launcher.Start_SiblingOrchestration(parent.OrchId, CHILD_NAME, _worktree, $"{parent.OrchId}#14");
        _store.Set_ImplementerEffortOverride(child.OrchId, "max");
        _spawner.SpawnedCommands.Clear();

        _launcher.Respawn_Implementer(child.OrchId, "solo-1");

        var spawn = Single_SoloSpawn(child.OrchId);
        Assert.Equal(_worktree, spawn.WorkingDirectory);
        Assert.Contains("--effort max", SpawnCommand_Builder.Decode_SessionScript(spawn));
    }

    /// <summary>TODAY'S BEHAVIOUR, PINNED: a session with no working path spawns and respawns where it always did.</summary>
    [Fact]
    public void AnUnlinkedSolo_StillSpawnsAtRepoPath()
    {
        _spawner.SpawnedCommands.Clear();

        var solo = _launcher.Start_BasicOrchestration("AIOrchestrator", _tempRepo);
        Assert.Equal(_tempRepo, Single_SoloSpawn(solo.OrchId).WorkingDirectory);

        _spawner.SpawnedCommands.Clear();
        _launcher.Respawn_Implementer(solo.OrchId, "solo-1");

        Assert.Equal(_tempRepo, Single_SoloSpawn(solo.OrchId).WorkingDirectory);
    }

    /// <summary>
    /// THE RESUME DECISION IS UNTOUCHED — only the directory it is made in moved. A probe naming a live
    /// transcript still yields `--resume &lt;id&gt;`, and it does so IN THE WORKTREE, which is the pairing
    /// Claude Code needs to find that conversation again (§7.1).
    /// </summary>
    [Fact]
    public void TheResumeIdPath_IsUnchanged()
    {
        var parent = _launcher.Start_BasicOrchestration("AIOrchestrator", _tempRepo);
        var child = _launcher.Start_SiblingOrchestration(parent.OrchId, CHILD_NAME, _worktree, $"{parent.OrchId}#14");

        var transcript = Path.Combine(_tempRoot, "projects", $"{SOLO_SESSION_ID}.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(transcript)!);
        File.WriteAllText(transcript, """{"type":"summary","summary":"a conversation"}""" + "\n");

        OrchestrationLauncherTests.Write_ProbeFile(
            Path.Combine(_paths.Get_ImplementerFolder(child.OrchId, "solo-1"), UsageTotals_Reader.SESSION_USAGE_FILE),
            SOLO_SESSION_ID,
            transcript);
        _spawner.SpawnedCommands.Clear();

        _launcher.Respawn_Implementer(child.OrchId, "solo-1");

        var spawn = Single_SoloSpawn(child.OrchId);
        Assert.Equal(_worktree, spawn.WorkingDirectory);
        Assert.Contains($"claude --resume {SOLO_SESSION_ID} ", SpawnCommand_Builder.Decode_SessionScript(spawn));
    }

    ISpawnCommand Single_SoloSpawn(string orchId)
    {
        return Assert.Single(_spawner.SpawnedCommands, command => SpawnCommand_Builder.Decode_SessionScript(command).Contains($"'/solo {orchId}'"));
    }

    /// <summary>
    /// Edits session.json on disk, as BasicOrchestrationWatchdogTests does: the store has no API for an
    /// old spawn stamp, and an old stamp is what every member looks like after it has run a while.
    /// </summary>
    void Age_MemberSpawn(string orchId)
    {
        var file = _paths.Get_SessionFile(orchId);
        var root = JsonNode.Parse(File.ReadAllText(file))!.AsObject();

        foreach (var member in root["members"]!.AsArray())
            member!["spawnedUtc"] = DateTime.UtcNow.AddHours(-2).ToString("O");

        File.WriteAllText(file, root.ToJsonString());
    }
}
