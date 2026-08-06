using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Sessions;

/// <summary>
/// Allocates orchestration ids automatically: '&lt;repo-slug&gt;-&lt;n&gt;' with n incremental per repo,
/// derived from the folders already on disk (no counter file to corrupt). The owner never types ids.
/// </summary>
public static class OrchId_Allocator
{
    public static string Allocate_NextOrchId(ISupervisionPaths paths, string repoName)
    {
        var slug = Build_Slug(repoName);
        var nextNumber = 1;

        if (Directory.Exists(paths.Root))
        {
            foreach (var folder in Directory.EnumerateDirectories(paths.Root))
            {
                var folderName = Path.GetFileName(folder);

                if (!folderName.StartsWith($"{slug}-", StringComparison.OrdinalIgnoreCase))
                    continue;

                var suffix = folderName[(slug.Length + 1)..];

                if (int.TryParse(suffix, out var number) && number >= nextNumber)
                    nextNumber = number + 1;
            }
        }

        return $"{slug}-{nextNumber}";
    }

    static string Build_Slug(string repoName)
    {
        var characters = repoName.ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-');

        var collapsed = new string([.. characters]);

        while (collapsed.Contains("--", StringComparison.Ordinal))
            collapsed = collapsed.Replace("--", "-");

        var slug = collapsed.Trim('-');

        if (slug.Length == 0)
            throw new Exception($"Repo name '{repoName}' produced an empty orchestration slug");

        return slug;
    }
}
