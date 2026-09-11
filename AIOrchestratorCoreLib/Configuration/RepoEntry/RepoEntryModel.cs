namespace AIOrchestratorCoreLib.Configuration.RepoEntry;

internal sealed class RepoEntryModel(string name, string path, int? topicColor) : IRepoEntry
{
    public string Name { get; } = name;
    public string Path { get; } = path;
    public int? TopicColor { get; } = topicColor;
}
