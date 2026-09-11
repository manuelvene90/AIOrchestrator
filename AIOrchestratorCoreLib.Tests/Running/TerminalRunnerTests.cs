using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.Running.SessionRunner;
using AIOrchestratorCoreLib.Spawning;
using AIOrchestratorCoreLib.Spawning.SessionSpawner;
using AIOrchestratorCoreLib.Spawning.SpawnCommand;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>The terminal runner builds exactly the command the launcher used to build — the move changed nothing.</summary>
public class TerminalRunnerTests
{
    sealed class RecordingSpawner : ISessionSpawner
    {
        public List<ISpawnCommand> Commands { get; } = [];

        public int? Spawn(ISpawnCommand command)
        {
            Commands.Add(command);
            return 1;
        }
    }

    [Theory]
    [InlineData(SessionRoles.Supervisor, "sup", "/supervisor orch-1")]
    [InlineData(SessionRoles.Implementer, "imp-1", "/implementer orch-1/imp-1")]
    [InlineData(SessionRoles.Reviewer, "rev-1", "/reviewer orch-1/rev-1")]
    [InlineData(SessionRoles.Solo, "solo-1", "/solo orch-1")]
    [InlineData(SessionRoles.Communicator, "com", "/communicator orch-1")]
    [InlineData(SessionRoles.General, "general", "/general-supervisor")]
    public void Start_SpawnsTheRoleCommand_ThroughTheSpawner(SessionRoles role, string memberId, string roleCommand)
    {
        var spawner = new RecordingSpawner();
        var runner = SessionRunner_Factory.Create_Terminal(spawner);

        runner.Start(SessionLaunch_Factory.Create(role, "orch-1", memberId, "/repo", "opus", "/pid", "Name"));

        Assert.Equal(SessionRunners.Terminal, runner.Kind);
        var script = SpawnCommand_Builder.Decode_SessionScript(Assert.Single(spawner.Commands));
        Assert.Contains($"'{roleCommand}'", script);
        Assert.Contains($"$env:AIORCH_ROLE='{SessionRole_Names.Get_EnvWord(role)}'", script);
        Assert.Contains($"$env:AIORCH_MEMBER='{memberId}'", script);
    }

    [Fact]
    public void Launch_RefusesEmptyIds()
    {
        Assert.Throws<ArgumentException>(() => SessionLaunch_Factory.Create(SessionRoles.Implementer, "", "imp-1", "/repo", null, "/pid", null));
        Assert.Throws<ArgumentException>(() => SessionLaunch_Factory.Create(SessionRoles.Implementer, "o", "imp-1", "", null, "/pid", null));
    }
}
