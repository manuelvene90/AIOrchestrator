namespace ClaudeContract.Tests;

/// <summary>
/// Finds the repository root (the folder holding <c>AIOrchestrator.slnx</c>) by walking up from
/// the test assembly, and from it the two things the suite needs on disk: the fake's binary and
/// the folder the Live runs write their evidence into.
///
/// A harness that cannot find what it tests must REFUSE TO RUN (CLAUDE.md decision 20): every
/// lookup here throws with the paths it tried rather than returning a guess.
/// </summary>
public static class Repo_Locator
{
    public const string SOLUTION_FILE = "AIOrchestrator.slnx";
    public const string LAST_RUN_FOLDER_NAME = "last-run";

    public static string Find_RepoRoot()
    {
        var folder = Path.GetDirectoryName(typeof(Repo_Locator).Assembly.Location);

        while (!string.IsNullOrEmpty(folder))
        {
            if (File.Exists(Path.Combine(folder, SOLUTION_FILE)))
                return folder;

            folder = Path.GetDirectoryName(folder);
        }

        throw new Exception($"No '{SOLUTION_FILE}' found above '{typeof(Repo_Locator).Assembly.Location}' — the contract tests must run from a checkout of the repository");
    }

    /// <summary>`tools/claude-contract/last-run/&lt;stamp&gt;/` — gitignored; one folder per Live run.</summary>
    public static string Create_LastRunFolder(string runLabel)
    {
        var folder = Path.Combine(Find_RepoRoot(), "tools", "claude-contract", LAST_RUN_FOLDER_NAME, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{runLabel}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    /// The fake's DLL: beside the test assembly (the ProjectReference copies it there) or, failing
    /// that, in the fake's own build output for the same configuration.
    /// </summary>
    public static string Find_FakeClaudeDll()
    {
        var testFolder = Path.GetDirectoryName(typeof(Repo_Locator).Assembly.Location) ?? string.Empty;
        var configuration = Path.GetFileName(Path.GetDirectoryName(testFolder)) ?? "Debug";

        string[] candidates =
        [
            Path.Combine(testFolder, "FakeClaude.dll"),
            Path.Combine(Find_RepoRoot(), "tools", "claude-contract", "FakeClaude", "bin", configuration, "net10.0", "FakeClaude.dll"),
        ];

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        throw new Exception($"FakeClaude.dll not found — build tools/claude-contract/FakeClaude first. Tried: {string.Join(", ", candidates)}");
    }
}
