using AIOrchestratorCoreLib.Configuration.RepoEntry;

namespace AIOrchestratorCoreLib.GeneralSupervision;

/// <summary>
/// Resolves the owner's informal repo name ("skeleton client") against the configured repo list:
/// exact name match first, then unique substring match. Ambiguity resolves to null — the general
/// supervisor asks the owner instead of the app guessing.
/// </summary>
public static class RepoQuery_Resolver
{
    public static IRepoEntry? Resolve_OrNull(string query, IReadOnlyList<IRepoEntry> repos)
    {
        var normalizedQuery = Normalize(query);

        foreach (var repo in repos)
        {
            if (Normalize(repo.Name) == normalizedQuery)
                return repo;
        }

        IRepoEntry? uniqueMatch = null;

        foreach (var repo in repos)
        {
            if (!Normalize(repo.Name).Contains(normalizedQuery, StringComparison.Ordinal))
                continue;

            if (uniqueMatch != null)
                return null;

            uniqueMatch = repo;
        }

        return uniqueMatch;
    }

    static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant().Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty);
    }
}
