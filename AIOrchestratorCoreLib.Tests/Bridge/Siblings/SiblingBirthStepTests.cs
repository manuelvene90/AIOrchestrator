using AIOrchestratorCoreLib.Bridge.Siblings;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.GeneralSupervision.SpawnSiblingRequest;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.Siblings;

/// <summary>
/// THE BIRTH STEP (spec 2026-09-23 §4.3 steps 2-5) without an engine: a real store, the real launcher
/// over <see cref="RecordingSpawner_Fake"/>, and a plain folder for the worktree. Whether that folder is
/// a worktree of the repo is the validator's question, asked before this step runs; the launcher checks
/// only that it exists, which is all the failure test below needs.
///
/// <para>
/// THE STEP APPENDS NOTHING. It returns the words and the engine appends them, so every assertion on
/// wording here is on the returned tuple, and a channel that stayed untouched is part of the contract.
/// </para>
/// </summary>
public class SiblingBirthStepTests : IDisposable
{
    const string CHILD_NAME = "AI-Orch · limits rework";
    const string PARENT_NAME = "AI-Orch · settings work";
    const string JOB = "Rework the usage-limit pause so a restored pause can be lifted per window";
    const int HANDOVER = 14;

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly string _worktree;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly RecordingSpawner_Fake _spawner;
    readonly IOrchestrationLauncher _launcher;

    public SiblingBirthStepTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-sibling-birth-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

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
        GC.SuppressFinalize(this);
    }

    /// <summary>§4.3 step 2: a parent getting its first sibling becomes the endeavour, under its own id.</summary>
    [Fact]
    public void AnUnlinkedParent_GetsItsOwnIdAsEndeavourId()
    {
        var parent = Start_Parent();

        var birth = SiblingBirth_Step.Execute(_store, _launcher, _paths, Request(parent.OrchId));

        Assert.Equal(parent.OrchId, _store.Get_Session(parent.OrchId).EndeavourId);
        Assert.Equal(parent.OrchId, _store.Get_Session(birth.Child.OrchId).EndeavourId);
    }

    /// <summary>A parent already in an endeavour (a sibling itself) is not re-stamped: the child joins that endeavour.</summary>
    [Fact]
    public void ALinkedParent_KeepsItsEndeavourId()
    {
        var parent = Start_Parent();
        _store.Set_EndeavourId(parent.OrchId, "ai-orchestrator-3");

        var birth = SiblingBirth_Step.Execute(_store, _launcher, _paths, Request(parent.OrchId));

        Assert.Equal("ai-orchestrator-3", _store.Get_Session(parent.OrchId).EndeavourId);
        Assert.Equal("ai-orchestrator-3", _store.Get_Session(birth.Child.OrchId).EndeavourId);
    }

    /// <summary>Asserted on what the SPAWNER was handed, because the title and the cwd are read at spawn.</summary>
    [Fact]
    public void TheChild_IsStartedWithTheRequestedNameAndTree()
    {
        var parent = Start_Parent();
        _spawner.SpawnedCommands.Clear();

        var birth = SiblingBirth_Step.Execute(_store, _launcher, _paths, Request(parent.OrchId));

        var spawn = Assert.Single(_spawner.SpawnedCommands);
        Assert.Equal(_worktree, spawn.WorkingDirectory);
        Assert.Contains(CHILD_NAME, RecordingSpawner_Fake.Read_Argument_After(spawn, "--title"), StringComparison.Ordinal);
        Assert.Equal(CHILD_NAME, _store.Get_Session(birth.Child.OrchId).DisplayName);
        Assert.Equal(_worktree, _store.Get_Session(birth.Child.OrchId).WorkingPath);
    }

    /// <summary>The idempotency key is spelled by <see cref="SiblingRequest_Validator.Format_HandoverKey"/> and by nobody else (decision 12).</summary>
    [Fact]
    public void TheBornFromHandoverKey_IsTheOneSpelling()
    {
        var parent = Start_Parent();

        var birth = SiblingBirth_Step.Execute(_store, _launcher, _paths, Request(parent.OrchId));
        var child = _store.Get_Session(birth.Child.OrchId);

        Assert.Equal(SiblingRequest_Validator.Format_HandoverKey(parent.OrchId, HANDOVER), child.BornFromHandover);
        Assert.Equal($"{parent.OrchId}#{HANDOVER}", child.BornFromHandover);
        Assert.Equal(parent.OrchId, child.BornFromOrchId);
    }

    /// <summary>
    /// The first message in the new topic says whose sibling it is, what the job is and where the brief
    /// lives — the one thing the child starts from is that entry of the PARENT's outbox.
    /// </summary>
    [Fact]
    public void TheBirthNote_NamesTheParent_TheJob_AndWhereTheBriefIs()
    {
        var parent = Start_Parent();
        _store.Set_DisplayName(parent.OrchId, PARENT_NAME);

        var birth = SiblingBirth_Step.Execute(_store, _launcher, _paths, Request(parent.OrchId));

        Assert.Equal($"🔗 Sibling of {PARENT_NAME}", birth.BirthNote.Subject);
        Assert.Equal(
            $"job: {JOB}\nWrite here about this job only.\nYour brief: {_paths.Get_SiblingOutboxFile(parent.OrchId)} entry [{HANDOVER}]",
            birth.BirthNote.Body);

        Assert.Equal($"sibling '{birth.Child.OrchId}' started — {CHILD_NAME} (its own topic)", birth.ParentNotice.Subject);

        Assert.Contains(birth.Child.OrchId, birth.GeneralLine.Subject, StringComparison.Ordinal);
        Assert.Contains(parent.OrchId, birth.GeneralLine.Subject, StringComparison.Ordinal);
        Assert.Contains(CHILD_NAME, birth.GeneralLine.Body, StringComparison.Ordinal);
    }

    /// <summary>An unnamed parent is named by its id — the note never says "Sibling of " and stops.</summary>
    [Fact]
    public void TheBirthNote_OfAnUnnamedParent_UsesItsId()
    {
        var parent = Start_Parent();

        var birth = SiblingBirth_Step.Execute(_store, _launcher, _paths, Request(parent.OrchId));

        Assert.Equal($"🔗 Sibling of {parent.OrchId}", birth.BirthNote.Subject);
    }

    /// <summary>
    /// THE REORDER OF §4.3 STEPS 2-3, pinned. A launch that fails must not leave the parent linked to an
    /// endeavour of one: nothing would ever unlink it, and every endeavour surface would draw a group
    /// with a single member.
    /// </summary>
    [Fact]
    public void ALaunchFailure_Throws_AndLeavesTheParentUnlinked()
    {
        var parent = Start_Parent();
        var before = _store.Load_All().Count;
        var missing = Path.Combine(_tempRoot, "repo.worktrees", "never-created");

        Assert.ThrowsAny<Exception>(() => SiblingBirth_Step.Execute(_store, _launcher, _paths, Request(parent.OrchId, worktree: missing)));

        Assert.Null(_store.Get_Session(parent.OrchId).EndeavourId);
        Assert.Equal(before, _store.Load_All().Count);
    }

    /// <summary>The step returns words; the engine appends them (Task 9 owns the order). Neither channel moved.</summary>
    [Fact]
    public void TheStep_AppendsToNoChannel()
    {
        var parent = Start_Parent();
        var parentChannel = File.ReadAllText(_paths.Get_OwnerChannelFile(parent.OrchId));

        var birth = SiblingBirth_Step.Execute(_store, _launcher, _paths, Request(parent.OrchId));

        Assert.Equal(parentChannel, File.ReadAllText(_paths.Get_OwnerChannelFile(parent.OrchId)));
        Assert.DoesNotContain("Sibling of", File.ReadAllText(_paths.Get_OwnerChannelFile(birth.Child.OrchId)), StringComparison.Ordinal);
    }

    IOrchestrationSession Start_Parent()
    {
        return _launcher.Start_BasicOrchestration("AIOrchestrator", _tempRepo);
    }

    ISpawnSiblingRequest Request(string parentOrchId, string? worktree = null)
    {
        return SpawnSiblingRequest_Factory.Create(
            parentOrchId,
            CHILD_NAME,
            JOB,
            HANDOVER,
            worktree ?? _worktree,
            "two jobs the owner wants to steer separately; disjoint files",
            Path.Combine(_paths.RequestsFolder, "sibling-test.json"));
    }
}
