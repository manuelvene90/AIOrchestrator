namespace AIOrchestratorCoreLib.Running.RoleRunnerConfig;

public static class RoleRunnerConfig_Factory
{
    public static IRoleRunnerConfig Create(SessionRunners runner, ResumeModes resume, string? permissionMode, string? settings = null, WakeModes wake = WakeModes.Watcher, Channels.StatusLog.BookkeepingSinks bookkeeping = Channels.StatusLog.BookkeepingSinks.Channel)
    {
        return new RoleRunnerConfigModel(
            runner,
            resume,
            string.IsNullOrWhiteSpace(permissionMode) ? null : permissionMode.Trim(),
            string.IsNullOrWhiteSpace(settings) ? null : settings.Trim(),
            wake,
            bookkeeping);
    }

    /// <summary>
    /// Terminal everywhere — the shape the app has always had — and Fresh only for the general
    /// supervisor, which is stateless across launches by owner directive (CLAUDE.md decision 8).
    /// Wake defaults to the watcher: a machine that states nothing keeps the bash monitor it has today,
    /// and its bookkeeping stays in the channel it has always been re-read from.
    /// </summary>
    public static IRoleRunnerConfig Create_Default(SessionRoles role)
    {
        return Create(SessionRunners.Terminal, role == SessionRoles.General ? ResumeModes.Fresh : ResumeModes.Transcript, null, wake: WakeModes.Watcher, bookkeeping: Channels.StatusLog.BookkeepingSinks.Channel);
    }
}
