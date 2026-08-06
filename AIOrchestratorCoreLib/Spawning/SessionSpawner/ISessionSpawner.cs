using AIOrchestratorCoreLib.Spawning.SpawnCommand;

namespace AIOrchestratorCoreLib.Spawning.SessionSpawner;

/// <summary>Executes a spawn command, preferring Windows Terminal, falling back to plain PowerShell.</summary>
public interface ISessionSpawner
{
    /// <summary>
    /// Returns the started process id, or null when it could not be determined.
    /// NOTE: wt.exe delegates to an existing terminal window and exits, so a wt pid is NOT a
    /// liveness signal — the UI derives liveness from channel activity instead.
    /// </summary>
    int? Spawn(ISpawnCommand command);
}
