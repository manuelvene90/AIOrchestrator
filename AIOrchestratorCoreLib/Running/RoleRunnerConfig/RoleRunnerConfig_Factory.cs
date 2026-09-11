namespace AIOrchestratorCoreLib.Running.RoleRunnerConfig;

public static class RoleRunnerConfig_Factory
{
    public static IRoleRunnerConfig Create(SessionRunners runner, ResumeModes resume, string? permissionMode, string? settings = null)
    {
        return new RoleRunnerConfigModel(
            runner,
            resume,
            string.IsNullOrWhiteSpace(permissionMode) ? null : permissionMode.Trim(),
            string.IsNullOrWhiteSpace(settings) ? null : settings.Trim());
    }

    /// <summary>
    /// Terminal everywhere — the shape the app has always had — and Fresh only for the general
    /// supervisor, which is stateless across launches by owner directive (CLAUDE.md decision 8).
    /// </summary>
    public static IRoleRunnerConfig Create_Default(SessionRoles role)
    {
        return Create(SessionRunners.Terminal, role == SessionRoles.General ? ResumeModes.Fresh : ResumeModes.Transcript, null);
    }
}
