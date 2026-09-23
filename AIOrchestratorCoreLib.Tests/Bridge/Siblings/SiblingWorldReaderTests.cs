using System.Text;
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Bridge.Siblings;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.GeneralSupervision.SpawnSiblingRequest;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.Status;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.Siblings;

/// <summary>
/// THE WORLD THE VALIDATOR JUDGES, gathered from disk and git. The validator is pure, so every fact it
/// can get wrong by being handed the wrong slice of the world is pinned here instead.
/// </summary>
public class SiblingWorldReaderTests : IDisposable
{
    const string REQUESTER_ID = "ai-orchestrator-7";

    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;

    public SiblingWorldReaderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-sibling-world-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        _paths = SupervisionPaths_Factory.Create(Path.Combine(_tempRoot, "supervision"));
        _store = OrchestrationSessionStore_Factory.Create(_paths);
    }

    public void Dispose()
    {
        TempTree.Delete_BestEffort(_tempRoot);
    }

    /// <summary>
    /// THE HANDOVER ENTRY MAY HAVE BEEN COMPACTED. The request can wait on disk through a usage-limit
    /// pause (§4.2), and a busy outbox compacts above 90 entries, moving [14] into
    /// sibling-outbox.archive.md. Reading the live file alone would refuse a perfectly good request as
    /// no-handover-entry. History goes through ChannelHistory_Counter (decision 13).
    /// </summary>
    [Fact]
    public void TheHandoverEntry_IsFound_AfterCompaction()
    {
        var repo = Path.Combine(_tempRoot, "plain-repo");
        Directory.CreateDirectory(repo);
        _store.Create_Orchestration(REQUESTER_ID, "AIOrchestrator", repo);

        var outbox = _paths.Get_SiblingOutboxFile(REQUESTER_ID);
        File.WriteAllText(outbox, Build_Outbox(100, handoverIndex: 14));

        Assert.NotNull(Channel_Compactor.Compact_IfNeeded(outbox));

        // THE COMPACTION REALLY MOVED IT — otherwise this test would pass for the live-file reason and
        // keep passing if the reader went back to reading the live file alone (decision 20).
        Assert.DoesNotContain(ChannelEntry_Parser.Parse_All(File.ReadAllText(outbox)), entry => entry.Index == 14);

        var world = Read(Request(Path.Combine(_tempRoot, "anywhere")));

        var handover = Assert.Single(world.RequesterOutboxHistory, entry => entry.Index == 14);
        Assert.True(MemberState_Resolver.Contains_Marker(handover, HandoverEntry_Detector.HANDOVER_MARKER));
    }

    [Fact]
    public void TheWorktreeList_ComesFromGit()
    {
        var (repo, worktree) = GitWorktree_Tool.Create_RepoWithWorktree(Path.Combine(_tempRoot, "git"), "limits");
        _store.Create_Orchestration(REQUESTER_ID, "AIOrchestrator", repo);
        File.WriteAllText(_paths.Get_SiblingOutboxFile(REQUESTER_ID), Build_Outbox(14, handoverIndex: 14));

        var world = Read(Request(worktree));

        Assert.Contains(world.RepoWorktreePaths, path => WorkingPath_Comparer.Are_Same(path, worktree));
        Assert.Contains(world.RepoWorktreePaths, path => WorkingPath_Comparer.Are_Same(path, repo));
        Assert.True(world.WorktreeExists);

        // END TO END: a real worktree of the requester's repo, a real HANDOVER entry — the table passes.
        Assert.Null(SiblingRequest_Validator.Decide_Refusal_OrNull(Request(worktree), world));
    }

    /// <summary>A PARKED REQUEST MUST NOT REFUSE ITSELF at tap time as "already held" — its own file is excluded.</summary>
    [Fact]
    public void OwnParkedFile_IsNotCountedAsAnotherParkedRequest()
    {
        var repo = Path.Combine(_tempRoot, "plain-repo");
        Directory.CreateDirectory(repo);
        _store.Create_Orchestration(REQUESTER_ID, "AIOrchestrator", repo);

        var awaiting = CloseConfirmation_Parking.Get_AwaitingFolder(_paths);
        Directory.CreateDirectory(awaiting);

        var own = Path.Combine(awaiting, "sibling-own.json");
        var other = Path.Combine(awaiting, "sibling-other.json");
        var unrelated = Path.Combine(awaiting, "close-something.json");

        File.WriteAllText(own, Build_SpawnSiblingJson(14));
        File.WriteAllText(other, Build_SpawnSiblingJson(20));
        File.WriteAllText(unrelated, new JsonObject { ["action"] = "close-orchestration", ["orchId"] = REQUESTER_ID, ["reason"] = "done" }.ToJsonString());

        // The own path spelled differently from the listing: the exclusion compares trees, not strings.
        var world = SiblingWorld_Reader.Read(
            _paths, _store, OrchestratorConfigProvider_Factory.Create(_paths).Get_Current(),
            Request(Path.Combine(_tempRoot, "anywhere")), own.Replace('\\', '/').ToUpperInvariant());

        var parked = Assert.Single(world.OtherParkedSiblingRequests);
        Assert.Equal(20, parked.HandoverIndex);
    }

    /// <summary>
    /// NOTHING KEYED ON AN UNKNOWN ID (Task 6 carry): no requester in the store means no outbox read and
    /// no git process — the raw id is only ever compared, never turned into a path.
    ///
    /// <para>
    /// "RUNS NO GIT" IS COUNTED, not inferred from an empty list (fix round 1, decision 20): git that ran
    /// and listed nothing leaves the same empty list, so the lister is injected and its calls counted.
    /// The companion test below proves the seam is live — a zero from a seam nothing calls would be the
    /// same two-routes trap one level down.
    /// </para>
    /// </summary>
    [Fact]
    public void AMissingRequester_ReadsNoOutbox_AndRunsNoGit()
    {
        List<string> gitCalls = [];

        var world = Read_Counting(Request(Path.Combine(_tempRoot, "anywhere")), gitCalls);

        Assert.Null(world.Requester);
        Assert.Empty(world.RequesterOutboxHistory);
        Assert.Empty(gitCalls);
        Assert.False(Directory.Exists(_paths.Get_OrchestrationFolder(REQUESTER_ID)));
    }

    [Fact]
    public void AnExistingRequester_AsksGitOnce_ForItsOwnRepo()
    {
        var repo = Path.Combine(_tempRoot, "plain-repo");
        Directory.CreateDirectory(repo);
        _store.Create_Orchestration(REQUESTER_ID, "AIOrchestrator", repo);
        List<string> gitCalls = [];

        var world = Read_Counting(Request(Path.Combine(_tempRoot, "anywhere")), gitCalls);

        Assert.Equal([repo], gitCalls);
        Assert.Equal(["listed-by-the-fake"], world.RepoWorktreePaths);
    }

    SiblingWorld Read_Counting(ISpawnSiblingRequest request, List<string> gitCalls)
    {
        return SiblingWorld_Reader.Read(
            _paths, _store, OrchestratorConfigProvider_Factory.Create(_paths).Get_Current(), request, null,
            repoPath =>
            {
                gitCalls.Add(repoPath);
                return ["listed-by-the-fake"];
            });
    }

    [Fact]
    public void TheCap_ComesFromTheConfig()
    {
        var world = Read(Request(Path.Combine(_tempRoot, "anywhere")));

        Assert.Equal(OrchestratorConfigProvider_Factory.Create(_paths).Get_Current().Endeavour.MaxOpenSiblings, world.MaxOpenMembers);
    }

    SiblingWorld Read(ISpawnSiblingRequest request)
    {
        return SiblingWorld_Reader.Read(_paths, _store, OrchestratorConfigProvider_Factory.Create(_paths).Get_Current(), request, null);
    }

    static ISpawnSiblingRequest Request(string worktree)
    {
        return SpawnSiblingRequest_Factory.Create(
            REQUESTER_ID, "AI-Orch · limits rework", "Rework the usage-limit pause", 14, worktree, "two jobs", "sibling-this.json");
    }

    string Build_SpawnSiblingJson(int handover)
    {
        return new JsonObject
        {
            ["action"] = OrchestrationRequests_Reader.SPAWN_SIBLING_ACTION,
            ["orchId"] = REQUESTER_ID,
            ["name"] = "AI-Orch · limits rework",
            ["job"] = "Rework the usage-limit pause",
            ["handover"] = handover,
            ["worktree"] = Path.Combine(_tempRoot, "anywhere"),
            ["reason"] = "two jobs",
        }.ToJsonString();
    }

    static string Build_Outbox(int entryCount, int handoverIndex)
    {
        var text = new StringBuilder();

        for (var index = 1; index <= entryCount; index++)
        {
            var subject = index == handoverIndex ? "HANDOVER — the limits job" : $"note {index}";
            text.Append($"\n## [{index}] FROM solo — 2026-09-23 10:00 — {subject}\n\nbody {index}\n");
        }

        return text.ToString();
    }
}
