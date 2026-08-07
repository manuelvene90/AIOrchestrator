using System.Diagnostics;
using System.Text;
using AIOrchestratorCoreLib.Git.GitSnapshot;

namespace AIOrchestratorCoreLib.Git;

/// <summary>
/// Reads what a working tree ACTUALLY contains, by running git directly. This is the antidote to
/// agent prose: "done, branch ready to merge" becomes verifiable — real commits, real dirty
/// files, real ahead/behind. Read-only commands only; every failure degrades to
/// "not a repository" rather than throwing into a UI refresh or a Telegram reply.
/// </summary>
public static class GitSnapshot_Reader
{
    const int GIT_TIMEOUT_MILLISECONDS = 8_000;
    const int DEFAULT_COMMIT_COUNT = 8;

    public static IGitSnapshot Read(string workingTreePath, int commitCount = DEFAULT_COMMIT_COUNT)
    {
        var shortPath = Build_ShortPath(workingTreePath);

        if (!Directory.Exists(workingTreePath))
            return GitSnapshot_Factory.Create_NotARepository(workingTreePath, shortPath);

        var insideRepo = Run_Git_OrNull(workingTreePath, "rev-parse --is-inside-work-tree");

        if (insideRepo == null || !insideRepo.Trim().StartsWith("true", StringComparison.OrdinalIgnoreCase))
            return GitSnapshot_Factory.Create_NotARepository(workingTreePath, shortPath);

        var branch = Run_Git_OrNull(workingTreePath, "rev-parse --abbrev-ref HEAD")?.Trim() ?? "";
        var status = Run_Git_OrNull(workingTreePath, "status --porcelain") ?? "";
        var log = Run_Git_OrNull(workingTreePath, $"log --oneline -{commitCount}") ?? "";
        var (ahead, behind) = Read_AheadBehind(workingTreePath);

        return GitSnapshot_Factory.Create(
            workingTreePath,
            shortPath,
            true,
            branch,
            ahead,
            behind,
            Count_NonEmptyLines(status),
            Split_NonEmptyLines(log));
    }

    /// <summary>Every tree an orchestration touches: the repo itself plus each linked worktree.</summary>
    public static IReadOnlyList<IGitSnapshot> Read_RepoAndWorktrees(string repoPath, int commitCount = DEFAULT_COMMIT_COUNT)
    {
        List<IGitSnapshot> snapshots = [Read(repoPath, commitCount)];

        foreach (var worktreePath in Find_WorktreePaths(repoPath))
        {
            if (string.Equals(Path.TrimEndingDirectorySeparator(worktreePath), Path.TrimEndingDirectorySeparator(repoPath), StringComparison.OrdinalIgnoreCase))
                continue;

            snapshots.Add(Read(worktreePath, commitCount));
        }

        return snapshots;
    }

    static IReadOnlyList<string> Find_WorktreePaths(string repoPath)
    {
        List<string> paths = [];

        var output = Run_Git_OrNull(repoPath, "worktree list --porcelain");

        if (output == null)
            return paths;

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');

            if (trimmed.StartsWith("worktree ", StringComparison.Ordinal))
                paths.Add(trimmed["worktree ".Length..].Trim());
        }

        return paths;
    }

    static (int Ahead, int Behind) Read_AheadBehind(string workingTreePath)
    {
        // Fails (and yields 0/0) when the branch has no upstream — the common worktree case.
        var output = Run_Git_OrNull(workingTreePath, "rev-list --left-right --count @{upstream}...HEAD");

        if (output == null)
            return (0, 0);

        var parts = output.Trim().Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 2 || !int.TryParse(parts[0], out var behind) || !int.TryParse(parts[1], out var ahead))
            return (0, 0);

        return (ahead, behind);
    }

    static string Build_ShortPath(string fullPath)
    {
        try
        {
            var trimmed = Path.TrimEndingDirectorySeparator(fullPath);
            var leaf = Path.GetFileName(trimmed);
            var parent = Path.GetFileName(Path.GetDirectoryName(trimmed) ?? "");

            return parent.Length == 0 ? leaf : Path.Combine(parent, leaf);
        }
        catch
        {
            return fullPath;
        }
    }

    static int Count_NonEmptyLines(string text)
    {
        return Split_NonEmptyLines(text).Count;
    }

    static IReadOnlyList<string> Split_NonEmptyLines(string text)
    {
        List<string> lines = [];

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();

            if (line.Length > 0)
                lines.Add(line);
        }

        return lines;
    }

    /// <summary>Runs a READ-ONLY git command; null on any failure (missing git, not a repo, timeout).</summary>
    static string? Run_Git_OrNull(string workingDirectory, string arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
            };

            using var process = Process.Start(startInfo);

            if (process == null)
                return null;

            var output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();

            if (!process.WaitForExit(GIT_TIMEOUT_MILLISECONDS))
            {
                Kill_BestEffort(process);
                return null;
            }

            if (process.ExitCode != 0)
                return null;

            return output;
        }
        catch
        {
            return null;
        }
    }

    static void Kill_BestEffort(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already gone.
        }
    }
}
