namespace AIOrchestratorCoreLib.Configuration.RepoEntry;

public static class RepoEntry_Factory
{
    public static IRepoEntry Create(string name, string path)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException($"Repo name must be non-empty (path was '{path}')");
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException($"Repo path must be non-empty (name was '{name}')");

        return new RepoEntryModel(name, path);
    }
}
