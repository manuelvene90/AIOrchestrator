namespace AIOrchestratorCoreLib.Running.RoleRunnerConfig;

internal sealed class RoleRunnerConfigModel(SessionRunners runner, ResumeModes resume, string? permissionMode, string? settings, WakeModes wake, Channels.StatusLog.BookkeepingSinks bookkeeping) : IRoleRunnerConfig
{
    public SessionRunners Runner { get; } = runner;
    public ResumeModes Resume { get; } = resume;
    public string? PermissionMode { get; } = permissionMode;
    public string? Settings { get; } = settings;
    public WakeModes Wake { get; } = wake;
    public Channels.StatusLog.BookkeepingSinks Bookkeeping { get; } = bookkeeping;
}
