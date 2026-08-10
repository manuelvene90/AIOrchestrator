using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.DiscoveredChannel;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Mirroring;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.Spawning;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Sessions;

/// <summary>
/// A BASIC orchestration is one session talking straight to the owner — no supervisor, reviewer or
/// gates. Like the other kinds it is derived from the MEMBER ID rather than stored, so session.json
/// needs no migration and an orchestration cannot disagree with itself about what it is.
/// </summary>
public class BasicOrchestrationTests : IDisposable
{
    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly RecordingSpawner_Fake _spawner;
    readonly IOrchestrationLauncher _launcher;

    public BasicOrchestrationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-basic-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        _spawner = new RecordingSpawner_Fake();

        _launcher = OrchestrationLauncher_Factory.Create(
            _paths,
            OrchestratorConfigProvider_Factory.Create(_paths),
            OrchestrationSessionStore_Factory.Create(_paths),
            _spawner,
            OrchestrationLog_Factory.Create(_paths));
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// The whole point: ONE terminal. A full orchestration spawns three (supervisor, imp-1, rev-1),
    /// and if this ever quietly grew one the mode would have lost its reason to exist.
    /// </summary>
    [Fact]
    public void Start_Basic_SpawnsExactlyOneSession_AndNoSupervisor()
    {
        var session = _launcher.Start_BasicOrchestration("Repo", _tempRepo);

        Assert.Equal("solo-1", Assert.Single(session.Members).MemberId);

        var script = SpawnCommand_Builder.Decode_SessionScript(Assert.Single(_spawner.SpawnedCommands));
        Assert.Contains($"/solo {session.OrchId}", script);
        Assert.Contains("AIORCH_ROLE='solo'", script);
        Assert.Null(session.SupervisorSpawnedUtc);
    }

    [Fact]
    public void Start_Orchestrated_IsUnaffected_AndIsNotBasic()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);

        Assert.Equal(3, _spawner.SpawnedCommands.Count);
        Assert.False(MemberKind_Ids.Is_BasicOrchestration(session.Members.Select(m => m.MemberId)));
    }

    [Theory]
    [InlineData("solo-1", MemberKinds.Solo)]
    [InlineData("SOLO-2", MemberKinds.Solo)]
    [InlineData("imp-1", MemberKinds.Implementer)]
    [InlineData("rev-1", MemberKinds.Reviewer)]
    public void TheIdCarriesTheKind(string memberId, MemberKinds expected)
    {
        Assert.Equal(expected, MemberKind_Ids.Resolve_Kind(memberId));
    }

    [Fact]
    public void Is_BasicOrchestration_ReadsItOffTheMembers()
    {
        Assert.True(MemberKind_Ids.Is_BasicOrchestration(["solo-1"]));
        Assert.False(MemberKind_Ids.Is_BasicOrchestration(["imp-1", "rev-1"]));
        Assert.False(MemberKind_Ids.Is_BasicOrchestration([]));
    }

    /// <summary>
    /// A respawned solo session must come back SOLO. Coming back as an implementer would leave it
    /// waiting for a supervisor that does not exist, i.e. silently dead.
    /// </summary>
    [Fact]
    public void Respawn_OfASoloSession_ComesBackSolo()
    {
        var started = _launcher.Start_BasicOrchestration("Repo", _tempRepo);
        _spawner.SpawnedCommands.Clear();

        _launcher.Respawn_Implementer(started.OrchId, "solo-1");

        var script = SpawnCommand_Builder.Decode_SessionScript(Assert.Single(_spawner.SpawnedCommands));
        Assert.Contains($"/solo {started.OrchId}", script);
    }

    /// <summary>
    /// The solo session writes on the OWNER channel by design, where an orchestrated member doing
    /// the same is a protocol violation worth flagging. Its voice must read as normal, not as "⚠".
    /// </summary>
    [Fact]
    public void SoloEntries_MirrorAsANormalVoice_UnlikeAStrayImplementer()
    {
        var ownerChannel = DiscoveredChannel_Factory.Create_ForOwner("basic-1", "path/owner-channel.md");

        var solo = Assert.Single(ChannelEntry_Parser.Parse_All("## [1] FROM solo — 2026-08-10 12:00 — done\nbuilt and tested"));
        Assert.Equal(ChannelAuthors.Solo, solo.Author);
        Assert.Equal("🟠 built and tested", MirrorText_Formatter.Format(ownerChannel, solo));

        var stray = Assert.Single(ChannelEntry_Parser.Parse_All("## [1] FROM implementer — 2026-08-10 12:00 — oops\nwrong channel"));
        Assert.StartsWith("⚠", MirrorText_Formatter.Format(ownerChannel, stray));
    }

    /// <summary>The solo voice is what the owner answers, so silence after it must count like a supervisor's.</summary>
    [Fact]
    public void TheSoloVoice_CountsForTheQuietAndAwayDetector()
    {
        Assert.True(ChannelAuthor_Kinds.Speaks_ToOwner(ChannelAuthors.Solo));
        Assert.True(ChannelAuthor_Kinds.Speaks_ToOwner(ChannelAuthors.Supervisor));
        Assert.False(ChannelAuthor_Kinds.Speaks_ToOwner(ChannelAuthors.Implementer));
        Assert.True(ChannelAuthor_Kinds.Is_Member(ChannelAuthors.Solo));
    }
}
