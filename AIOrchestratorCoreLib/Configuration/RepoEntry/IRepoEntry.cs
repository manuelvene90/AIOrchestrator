namespace AIOrchestratorCoreLib.Configuration.RepoEntry;

/// <summary>One repository the orchestrator can start a supervision session on.</summary>
public interface IRepoEntry
{
    string Name { get; }
    string Path { get; }
}
