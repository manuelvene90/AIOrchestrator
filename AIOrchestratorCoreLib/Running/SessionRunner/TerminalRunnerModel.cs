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
///
/// <para>
/// IT DECIDES NOTHING, INCLUDING THE TWO DIALS IT CARRIES. <c>Effort</c> and <c>ResumeSessionId</c>
/// arrive on the launch already resolved by the launcher — which role gets a default, whether this
/// slot has a transcript worth resuming — and are handed to the builder as they came. Only the
/// supervisor and the solo are given a resume id at all; every other role takes the effort and
/// nothing else, because they re-enter through their role command by design.
/// </para>
/// <para>
/// ALL SIX ROLES PASS <c>launch.Effort</c> SINCE 2026-09-12 (task-7 fix round 1). The communicator
/// and the general used to be handed to builder overloads that took no effort at all, so a resolved
/// <c>effort.communicator</c> / <c>effort.general</c> died here without a flag or a log line — a
/// runner that "decides nothing" was deciding null for two roles. A new role added to this switch
/// must pass it too: the launcher resolves an effort for every member of <c>SessionRole_Names.ALL</c>.
/// </para>
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
            SessionRoles.Supervisor => SpawnCommand_Builder.Build_ForSupervisor(launch.OrchId, launch.WorkingDirectory, launch.Model, launch.Effort, launch.ResumeSessionId, launch.PidFilePath, launch.DisplayName),
            SessionRoles.Communicator => SpawnCommand_Builder.Build_ForCommunicator(launch.OrchId, launch.WorkingDirectory, launch.Model, launch.Effort, launch.PidFilePath, launch.DisplayName),
            SessionRoles.Reviewer => SpawnCommand_Builder.Build_ForReviewer(launch.OrchId, launch.MemberId, launch.WorkingDirectory, launch.Model, launch.Effort, launch.PidFilePath, launch.DisplayName),
            SessionRoles.Solo => SpawnCommand_Builder.Build_ForSolo(launch.OrchId, launch.MemberId, launch.WorkingDirectory, launch.Model, launch.Effort, launch.ResumeSessionId, launch.PidFilePath, launch.DisplayName),
            SessionRoles.Implementer => SpawnCommand_Builder.Build_ForImplementer(launch.OrchId, launch.MemberId, launch.WorkingDirectory, launch.Model, launch.Effort, launch.PidFilePath, launch.DisplayName),
            SessionRoles.General => SpawnCommand_Builder.Build_ForGeneralSupervisor(launch.WorkingDirectory, launch.Model, launch.Effort, launch.PidFilePath),
            _ => throw new Exception($"Unhandled SessionRoles '{launch.Role}' starting '{launch.OrchId}/{launch.MemberId}'"),
        };
    }
}
