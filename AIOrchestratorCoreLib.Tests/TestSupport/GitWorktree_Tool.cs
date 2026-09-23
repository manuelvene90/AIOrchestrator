using System.Diagnostics;

namespace AIOrchestratorCoreLib.Tests.TestSupport;

/// <summary>
/// A REAL git repository with one REAL linked worktree, under a test's own temp folder — for the tests
/// whose claim is "the list comes from git", which a hand-written list cannot prove.
///
/// <para>
/// IT REFUSES TO RUN WITHOUT GIT (CLAUDE.md decision 20). A test that cannot start git and carries on
/// would assert that a worktree is absent from an empty list — true, green, and about nothing. So the
/// first command failing to START throws, naming why, instead of letting the test certify the absence
/// of the thing it exists to test.
/// </para>
/// <para>
/// THE ROOT IS CANONICALISED FIRST, because on Windows the temp folder may be an 8.3 short name
/// (<c>C:\Users\GIANPI~1\...</c> on the machine this was written on, 2026-09-23) while
/// <c>git worktree list</c> prints the long one. The returned paths are therefore spelled the way git
/// spells them, so a test compares one tree with itself rather than failing on a spelling the product
/// never sees from a session writing its own worktree path.
/// </para>
/// </summary>
internal static class GitWorktree_Tool
{
    const int GIT_TIMEOUT_MILLISECONDS = 30_000;

    public static (string RepoPath, string WorktreePath) Create_RepoWithWorktree(string root, string worktreeName)
    {
        Directory.CreateDirectory(root);

        var canonicalRoot = Resolve_LongPath(root);
        var repoPath = Path.Combine(canonicalRoot, "repo");
        Directory.CreateDirectory(repoPath);

        Run_Git(repoPath, "init -q", isFirstCommand: true);
        Run_Git(repoPath, "-c user.email=t@t -c user.name=t commit -q --allow-empty -m init", isFirstCommand: false);
        Run_Git(repoPath, $"worktree add -q ../{worktreeName} -b {worktreeName}", isFirstCommand: false);

        return (repoPath, Path.Combine(canonicalRoot, worktreeName));
    }

    static void Run_Git(string workingDirectory, string arguments, bool isFirstCommand)
    {
        Process? process;

        try
        {
            process = Process.Start(new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
        }
        catch (Exception ex) when (isFirstCommand)
        {
            throw new Exception("git not found on PATH — REFUSING to run a worktree test that would assert about nothing", ex);
        }

        if (process == null)
            throw new Exception("git not found on PATH — REFUSING to run a worktree test that would assert about nothing");

        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(GIT_TIMEOUT_MILLISECONDS))
                throw new Exception($"git {arguments} did not finish within {GIT_TIMEOUT_MILLISECONDS} ms in '{workingDirectory}'");

            if (process.ExitCode != 0)
                throw new Exception($"git {arguments} failed ({process.ExitCode}) in '{workingDirectory}': {stderr.Result}{stdout.Result}");
        }
    }

    /// <summary>
    /// Each segment re-read from its parent's listing: on Windows a short-name pattern matches the
    /// entry and the listing returns the long name; elsewhere it is an exact match and changes nothing.
    /// </summary>
    static string Resolve_LongPath(string path)
    {
        var full = Path.GetFullPath(path);
        var current = Path.GetPathRoot(full) ?? throw new Exception($"'{full}' has no root");

        foreach (var segment in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Directory.EnumerateFileSystemEntries(current, segment).FirstOrDefault();
            current = match ?? Path.Combine(current, segment);
        }

        return current;
    }
}
