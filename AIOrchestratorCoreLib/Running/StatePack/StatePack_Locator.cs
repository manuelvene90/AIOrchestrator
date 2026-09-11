using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.StatePack;

/// <summary>
/// Where a role's pack lives — beside the files that role already reads at boot, so the skill's
/// one sentence ("if `pack.md` exists in your folder, read it first") resolves the same way for
/// everyone: members and the solo in `<orch>/<member>/pack.md`, the supervisor next to its state
/// file as `<orch>/.supervisor.pack.md`, the general in its own folder.
/// </summary>
public static class StatePack_Locator
{
    public const string FILE_NAME = "pack.md";
    public const string SUPERVISOR_FILE_NAME = ".supervisor.pack.md";

    public static string Get_File(ISupervisionPaths paths, SessionRoles role, string orchId, string memberId)
    {
        return role switch
        {
            SessionRoles.General => Path.Combine(paths.GeneralFolder, FILE_NAME),
            SessionRoles.Supervisor => Path.Combine(paths.Get_OrchestrationFolder(orchId), SUPERVISOR_FILE_NAME),
            _ => Path.Combine(paths.Get_OrchestrationFolder(orchId), memberId, FILE_NAME),
        };
    }
}
