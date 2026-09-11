using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.Spawning;
using AIOrchestratorCoreLib.Spawning.SessionSpawner;
using AIOrchestratorCoreLib.Spawning.SpawnCommand;

namespace AIOrchestratorCoreLib.Running.SessionRunner;

/// <summary>
/// The shape this app has always had, wrapped: a Windows Terminal window per session, built by
/// <see cref="SpawnCommand_Builder"/> exactly as before and spawned by the existing
/// <see cref="ISessionSpawner"/>. Nothing about the command changed — the role switch that used
/// to live in the launcher lives here, and that is the whole move.
/// </summary>
internal sealed class TerminalRunnerModel(ISessionSpawner spawner) : ISessionRunner
{
    readonly ISessionSpawner _spawner = spawner;

    public SessionRunners Kind => SessionRunners.Terminal;

    public void Start(ISessionLaunch launch)
    {
        _spawner.Spawn(Build_Command(launch));
    }

    public static ISpawnCommand Build_Command(ISessionLaunch launch)
    {
        return launch.Role switch
        {
            SessionRoles.Supervisor => SpawnCommand_Builder.Build_ForSupervisor(launch.OrchId, launch.WorkingDirectory, launch.Model, launch.PidFilePath, launch.DisplayName),
            SessionRoles.Communicator => SpawnCommand_Builder.Build_ForCommunicator(launch.OrchId, launch.WorkingDirectory, launch.Model, launch.PidFilePath, launch.DisplayName),
            SessionRoles.Reviewer => SpawnCommand_Builder.Build_ForReviewer(launch.OrchId, launch.MemberId, launch.WorkingDirectory, launch.Model, launch.PidFilePath, launch.DisplayName),
            SessionRoles.Solo => SpawnCommand_Builder.Build_ForSolo(launch.OrchId, launch.MemberId, launch.WorkingDirectory, launch.Model, launch.PidFilePath, launch.DisplayName),
            SessionRoles.Implementer => SpawnCommand_Builder.Build_ForImplementer(launch.OrchId, launch.MemberId, launch.WorkingDirectory, launch.Model, launch.PidFilePath, launch.DisplayName),
            SessionRoles.General => SpawnCommand_Builder.Build_ForGeneralSupervisor(launch.WorkingDirectory, launch.Model, launch.PidFilePath),
            _ => throw new Exception($"Unhandled SessionRoles '{launch.Role}' starting '{launch.OrchId}/{launch.MemberId}'"),
        };
    }
}
