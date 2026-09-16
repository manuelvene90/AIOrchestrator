namespace AIOrchestratorCoreLib.Tests;

/// <summary>
/// The repo's own source, found from the test assembly's location. Scan tests read the SOURCE, which
/// is the point: they assert about code shape, not about behaviour, and there is nothing to run.
/// A walk that cannot find the tree THROWS rather than returning nothing — decision 20: a harness
/// that cannot find what it tests must refuse to run, never certify an absence it never looked at.
/// </summary>
public static class SourceTree
{
    public static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AIOrchestrator.slnx")))
                directory = directory.Parent;

            return directory?.FullName
                ?? throw new Exception($"Could not find the repo root above '{AppContext.BaseDirectory}' — this scan cannot judge code it has not read.");
        }
    }

    public static IEnumerable<string> EnumerateCSharp(string relativeFolder)
    {
        var folder = Path.Combine(Root, relativeFolder);

        if (!Directory.Exists(folder))
            throw new Exception($"'{folder}' does not exist — this scan cannot judge code it has not read.");

        foreach (var file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;

            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;

            yield return file;
        }
    }
}
