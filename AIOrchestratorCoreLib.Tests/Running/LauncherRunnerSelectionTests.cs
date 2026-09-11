using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Spawning.SessionSpawner;
using AIOrchestratorCoreLib.Spawning.SpawnCommand;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>A1.1 / A1.5 at the launcher: terminal by default, print where configured, terminal-with-a-warning where print is not yet supported.</summary>
public class LauncherRunnerSelectionTests
{
    sealed class RecordingSpawner : ISessionSpawner
    {
        public List<ISpawnCommand> Commands { get; } = [];

        public int? Spawn(ISpawnCommand command)
        {
            Commands.Add(command);
            return 77777;
        }
    }

    static (PrintRunnerTestHarness Harness, RecordingSpawner Spawner, IOrchestrationLauncher Launcher) Build(string printRoles)
    {
        var harness = new PrintRunnerTestHarness(printRoles);
        var spawner = new RecordingSpawner();
        var launcher = OrchestrationLauncher_Factory.Create(harness.Paths, harness.ConfigProvider, harness.Store, spawner, harness.Log);
        return (harness, spawner, launcher);
    }

    [Fact]
    public void DefaultConfig_SpawnsEverythingInTerminals_ExactlyAsBefore()
    {
        var (harness, spawner, launcher) = Build("");
        using (harness)
        {
            var session = launcher.Start_Orchestration("Repo", harness.RepoPath);

            Assert.Equal(3, spawner.Commands.Count);
            Assert.False(PrintSessionState_Store.Exists(harness.Paths, SessionRoles.Implementer, session.OrchId, "imp-1"));
            Assert.False(PrintSessionState_Store.Exists(harness.Paths, SessionRoles.Reviewer, session.OrchId, "rev-1"));
        }
    }

    [Fact]
    public void PrintImplementer_IsRegistered_NotSpawned_AndKeepsItsSessionOnRespawn()
    {
        var (harness, spawner, launcher) = Build("implementer");
        using (harness)
        {
            var session = launcher.Start_Orchestration("Repo", harness.RepoPath);

            // Supervisor + reviewer in terminals; the implementer registered.
            Assert.Equal(2, spawner.Commands.Count);
            Assert.True(PrintSessionState_Store.Exists(harness.Paths, SessionRoles.Implementer, session.OrchId, "imp-1"));
            var registered = harness.Read_State(SessionRoles.Implementer, session.OrchId, "imp-1");
            Assert.Equal(harness.RepoPath, registered.WorkingDirectory);
            Assert.Equal(harness.Paths.Get_ImplementerChannelFile(session.OrchId, "imp-1"), registered.ChannelFilePath);

            launcher.Respawn_Implementer(session.OrchId, "imp-1");

            Assert.Equal(2, spawner.Commands.Count);
            Assert.Equal(registered.SessionId, harness.Read_State(SessionRoles.Implementer, session.OrchId, "imp-1").SessionId);
        }
    }

    /// <summary>
    /// A PRINT supervisor is registered exactly like a stream one — this used to be the test that it was
    /// refused. What refused it was the single-channel trigger, and that is gone; the transports differ
    /// in latency and residency, never in what wakes a session.
    /// </summary>
    [Fact]
    public void PrintSupervisor_IsRegistered_NotSpawned_JustLikeAStreamOne()
    {
        var (harness, spawner, launcher) = Build("supervisor");
        using (harness)
        {
            var session = launcher.Start_Orchestration("Repo", harness.RepoPath);

            // The reviewer and the implementer still get windows; the supervisor does not.
            Assert.Equal(2, spawner.Commands.Count);
            Assert.True(PrintSessionState_Store.Exists(harness.Paths, SessionRoles.Supervisor, session.OrchId, "sup"));
            Assert.Equal(harness.Paths.Get_OwnerChannelFile(session.OrchId), harness.Read_State(SessionRoles.Supervisor, session.OrchId, "sup").ChannelFilePath);
        }
    }

    [Fact]
    public void StreamSupervisor_IsRegistered_WithACursorPerChannelItIsWokenBy()
    {
        var (harness, spawner, launcher) = Build("supervisor:stream");
        using (harness)
        {
            var logged = new List<string>();
            harness.Log.EntryLogged += entry => logged.Add(entry.Message);

            var session = launcher.Start_Orchestration("Repo", harness.RepoPath);

            // The reviewer and the implementer still get windows; the supervisor does not.
            Assert.Equal(2, spawner.Commands.Count);
            Assert.True(PrintSessionState_Store.Exists(harness.Paths, SessionRoles.Supervisor, session.OrchId, "sup"));

            var registered = harness.Read_State(SessionRoles.Supervisor, session.OrchId, "sup");
            Assert.Equal(harness.Paths.Get_OwnerChannelFile(session.OrchId), registered.ChannelFilePath);

            // THE SUPERVISOR IS REGISTERED BEFORE ITS MEMBERS EXIST, so at this instant the roster names
            // one channel and it carries one cursor. The spokes are picked up by the dispatcher as they
            // appear, and a source that appears mid-life starts EMPTY — so imp-1's boot greeting, landing
            // between its spawn and the next tick, is traffic and not absorbed history.
            Assert.Equal("owner", Assert.Single(registered.Cursors).SourceKey);
            Assert.Empty(registered.Cursors[0].Delivered);

            // Which channels wake it, said at every registration.
            Assert.Contains(logged, message => message.Contains("woken by 1 channel(s): owner", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ABgRole_HasNoTransportHere_SoItIsSpawnedInATerminal_WithAWarning()
    {
        var (harness, spawner, launcher) = Build("implementer:bg");
        using (harness)
        {
            var logged = new List<string>();
            harness.Log.EntryLogged += entry => logged.Add(entry.Message);

            var session = launcher.Start_Orchestration("Repo", harness.RepoPath);

            Assert.Equal(3, spawner.Commands.Count);
            Assert.False(PrintSessionState_Store.Exists(harness.Paths, SessionRoles.Implementer, session.OrchId, "imp-1"));
            Assert.Contains(logged, message => message.Contains("runner: bg", StringComparison.Ordinal) && message.Contains("no role in this stage", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void PrintGeneral_IsRegisteredInItsHome()
    {
        var (harness, spawner, launcher) = Build("general");
        using (harness)
        {
            launcher.Spawn_GeneralSupervisor();

            Assert.Empty(spawner.Commands);
            Assert.True(PrintSessionState_Store.Exists(harness.Paths, SessionRoles.General, ChannelDiscovery.GENERAL_ORCH_ID, "general"));
            Assert.Equal(harness.Paths.GeneralFolder, harness.Read_State(SessionRoles.General, ChannelDiscovery.GENERAL_ORCH_ID, "general").WorkingDirectory);
        }
    }

    [Fact]
    public void PrintSolo_IsRegistered_OnTheOwnerChannel()
    {
        var (harness, spawner, launcher) = Build("solo");
        using (harness)
        {
            var session = launcher.Start_BasicOrchestration("Repo", harness.RepoPath);
            var memberId = Assert.Single(session.Members).MemberId;

            Assert.Empty(spawner.Commands);
            Assert.Equal(MemberKinds.Solo, MemberKind_Ids.Resolve_Kind(memberId));
            Assert.Equal(harness.Paths.Get_OwnerChannelFile(session.OrchId), harness.Read_State(SessionRoles.Solo, session.OrchId, memberId).ChannelFilePath);
        }
    }
}
