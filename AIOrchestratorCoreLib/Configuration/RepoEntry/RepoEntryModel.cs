namespace AIOrchestratorCoreLib.Configuration.RepoEntry;

internal sealed class RepoEntryModel(string name, string path) : IRepoEntry
{
    public string Name { get; } = name;
    public string Path { get; } = path;
}
