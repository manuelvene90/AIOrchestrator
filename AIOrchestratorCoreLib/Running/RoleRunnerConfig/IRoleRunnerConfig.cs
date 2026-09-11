namespace AIOrchestratorCoreLib.Running.RoleRunnerConfig;

/// <summary>How one role's sessions are run — the per-role block under <c>runners</c> in config.json.</summary>
public interface IRoleRunnerConfig
{
    SessionRunners Runner { get; }
    ResumeModes Resume { get; }

    /// <summary>
    /// Passed as <c>--permission-mode</c> to a bridge-driven session. Null keeps today's behaviour,
    /// <c>--dangerously-skip-permissions</c> (owner directive: an unattended session must never
    /// hang on a prompt). The terminal runner does not read it.
    /// </summary>
    string? PermissionMode { get; }

    /// <summary>
    /// Passed as <c>--settings</c>: a settings FILE path, or inline JSON, exactly as the CLI takes
    /// it. Null for every role that says nothing — except <c>bg</c>, where silence is filled in with
    /// <see cref="BgSettings_Rule.CANONICAL_SETTINGS"/> by the loader, because that transport is
    /// never run with Remote Control on.
    /// </summary>
    string? Settings { get; }
}
