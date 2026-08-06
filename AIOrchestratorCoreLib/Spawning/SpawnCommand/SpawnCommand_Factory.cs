namespace AIOrchestratorCoreLib.Spawning.SpawnCommand;

public static class SpawnCommand_Factory
{
    public static ISpawnCommand Create(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(executable))
            throw new ArgumentException($"Executable must be non-empty (workingDirectory '{workingDirectory}')");

        return new SpawnCommandModel(executable, arguments, workingDirectory);
    }
}
