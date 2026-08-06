namespace AIOrchestratorCoreLib.Spawning.SpawnCommand;

internal sealed class SpawnCommandModel(
    string executable,
    IReadOnlyList<string> arguments,
    string workingDirectory) : ISpawnCommand
{
    public string Executable { get; } = executable;
    public IReadOnlyList<string> Arguments { get; } = arguments;
    public string WorkingDirectory { get; } = workingDirectory;
}
