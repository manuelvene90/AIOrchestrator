using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.Spawning;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Launching;

/// <summary>
/// The promotion itself: a basic orchestration becomes a full crew.
///
/// The design's central claim is that NOTHING MOVES — the solo has been writing `owner-channel.md`,
/// which is the file a supervisor owns, so the supervisor inherits the whole conversation by opening
/// the file it was already going to open. These cases pin the parts that are code: which sessions
/// exist afterwards, which one is closed, and that the channel and the topic were not touched.
/// </summary>
public class PromoteToFullCrewTests : IDisposable
{
    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly RecordingSpawner_Fake _spawner;
    readonly IOrchestrationLauncher _launcher;

    public PromoteToFullCrewTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-promote-crew-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _spawner = new RecordingSpawner_Fake();

        _launcher = OrchestrationLauncher_Factory.Create(
            _paths, OrchestratorConfigProvider_Factory.Create(_paths), _store, _spawner, OrchestrationLog_Factory.Create(_paths));
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// THE SHAPE AFTERWARDS: a supervisor running, imp-1 running, the solo closed. Asserted on the
    /// role commands the spawned sessions will actually run, not on tab titles.
    /// </summary>
    [Fact]
    public void ASupervisorAndAnEmptyImplementerReplaceTheSolo()
    {
        var basic = _launcher.Start_BasicOrchestration("repo", _tempRepo);
        var soloId = Assert.Single(basic.Members).MemberId;

        _spawner.SpawnedCommands.Clear();

        var promoted = _launcher.Promote_ToFullCrew(basic.OrchId);

        Assert.Contains(_spawner.SpawnedCommands, c => Script(c).Contains($"/supervisor {basic.OrchId}", StringComparison.Ordinal));
        Assert.Contains(_spawner.SpawnedCommands, c => Script(c).Contains($"/implementer {basic.OrchId}/imp-1", StringComparison.Ordinal));

        var solo = Assert.Single(promoted.Members, m => m.MemberId == soloId);
        Assert.NotNull(solo.ClosedUtc);

        var implementer = Assert.Single(promoted.Members, m => m.MemberId == "imp-1");
        Assert.Null(implementer.ClosedUtc);
    }

    /// <summary>
    /// IT STOPS READING AS BASIC, which is the whole point of step 2's derivation: the solo stays in
    /// the roster for ever as audit trail, so anything reading member ids would still call this basic
    /// and leave the new supervisor unprotected by the watchdog.
    /// </summary>
    [Fact]
    public void ThePromotedOrchestrationNoLongerReadsAsBasic()
    {
        var basic = _launcher.Start_BasicOrchestration("repo", _tempRepo);

        Assert.True(OrchestrationShape.Is_BasicOrchestration(basic.SupervisorSpawnedUtc));

        var promoted = _launcher.Promote_ToFullCrew(basic.OrchId);

        Assert.False(OrchestrationShape.Is_BasicOrchestration(promoted.SupervisorSpawnedUtc));

        // And the solo is still on the roster — the exact state that fooled the old derivation.
        Assert.Contains(promoted.Members, m => MemberKind_Ids.Resolve_Kind(m.MemberId) == MemberKinds.Solo);
    }

    /// <summary>
    /// NOTHING MOVES: the owner channel is the same file with the same contents, and the Telegram
    /// topic binding is untouched. This is the claim the whole design rests on, so it is asserted
    /// rather than described — the supervisor inherits the conversation by opening the file it was
    /// always going to open.
    /// </summary>
    [Fact]
    public void TheOwnerChannelAndTheTopicAreUntouched()
    {
        var basic = _launcher.Start_BasicOrchestration("repo", _tempRepo);
        _store.Set_TelegramTopicId(basic.OrchId, 4242);

        var channelFile = _paths.Get_OwnerChannelFile(basic.OrchId);
        File.AppendAllText(channelFile, "\n## [7] FROM solo — 2026-08-13 13:00 — HANDOVER — the parser is the hard part\n");

        var before = File.ReadAllText(channelFile);

        var promoted = _launcher.Promote_ToFullCrew(basic.OrchId);

        Assert.Equal(before, File.ReadAllText(channelFile));
        Assert.Equal(4242, promoted.TelegramTopicId);
    }

    /// <summary>
    /// The supervisor spawn comes FIRST, and the order is the design decision: its spawn stamps the
    /// orchestration as promoted before anything is destroyed, so a failure there leaves the solo
    /// running and the orchestration exactly what it was. Closing the solo first would mean a failed
    /// spawn leaves NOTHING running — the watchdog skips closed members, and a basic orchestration
    /// has no supervisor slot to protect.
    ///
    /// IT OBSERVES THE STORE AT THE MOMENT OF THE SPAWN, and the first version of this test did not.
    /// It compared the supervisor's spawn index against the IMPLEMENTER's — which is a different
    /// ordering entirely — so inverting the very decision it is named for left it green. I found that
    /// by running the mutation, not by reading the test.
    ///
    /// A spawner that reads the solo's state as each session starts is the only way to see the
    /// interleaving from outside: the close is a store write and the spawn is a call, and nothing
    /// else records both on one timeline.
    /// </summary>
    [Fact]
    public void TheSupervisorIsSpawnedBeforeTheSoloIsClosed()
    {
        var basic = _launcher.Start_BasicOrchestration("repo", _tempRepo);
        var soloId = Assert.Single(basic.Members).MemberId;

        var probe = new OrderingSpawner_Fake(() => _store.Get_Session(basic.OrchId).Members.Single(m => m.MemberId == soloId).ClosedUtc != null);

        var probedLauncher = OrchestrationLauncher_Factory.Create(
            _paths, OrchestratorConfigProvider_Factory.Create(_paths), _store, probe, OrchestrationLog_Factory.Create(_paths));

        probedLauncher.Promote_ToFullCrew(basic.OrchId);

        var supervisorSpawn = Assert.Single(probe.Spawns, spawn => spawn.Script.Contains("/supervisor ", StringComparison.Ordinal));

        Assert.False(supervisorSpawn.SoloWasClosed);

        // And the solo IS closed by the time imp-1 starts, so this cannot pass by nothing ever closing.
        var implementerSpawn = Assert.Single(probe.Spawns, spawn => spawn.Script.Contains("/implementer ", StringComparison.Ordinal));

        Assert.True(implementerSpawn.SoloWasClosed);
    }

    static string Script(AIOrchestratorCoreLib.Spawning.SpawnCommand.ISpawnCommand command)
    {
        return SpawnCommand_Builder.Decode_SessionScript(command);
    }

    /// <summary>
    /// Records what the STORE said at the instant each session was spawned — the only vantage point
    /// from which a spawn and a store write can be seen on one timeline.
    /// </summary>
    sealed class OrderingSpawner_Fake(Func<bool> isSoloClosedNow) : AIOrchestratorCoreLib.Spawning.SessionSpawner.ISessionSpawner
    {
        public List<(string Script, bool SoloWasClosed)> Spawns { get; } = [];

        public int? Spawn(AIOrchestratorCoreLib.Spawning.SpawnCommand.ISpawnCommand command)
        {
            Spawns.Add((SpawnCommand_Builder.Decode_SessionScript(command), isSoloClosedNow()));
            return 77777;
        }
    }
}
