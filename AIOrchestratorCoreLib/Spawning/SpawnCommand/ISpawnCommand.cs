namespace AIOrchestratorCoreLib.Spawning.SpawnCommand;

/// <summary>An executable plus its argument list, ready for Process.Start.</summary>
public interface ISpawnCommand
{
    string Executable { get; }
    IReadOnlyList<string> Arguments { get; }

    /// <summary>Working directory for the spawned process (the repo the session works in).</summary>
    string WorkingDirectory { get; }
}
