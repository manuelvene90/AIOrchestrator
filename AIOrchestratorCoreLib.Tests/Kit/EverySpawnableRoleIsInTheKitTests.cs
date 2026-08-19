using System.Text.RegularExpressions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// EVERY ROLE THE APP CAN SPAWN MUST SHIP WITH THE REPO.
///
/// `communicator.md` lived at ~/.claude/commands and nowhere else for an unknown length of time —
/// 84 lines, one machine, no backup — while `SpawnCommand_Builder` happily launched
/// `/communicator &lt;id&gt;` and its own comment pointed at the file. The csproj copies
/// `kit\commands\*.md`, a GLOB, so a file that was never added was never noticed: nothing failed,
/// nothing warned, and a fresh machine would simply have booted that session on an unknown command.
///
/// The owner found it by asking (2026-08-19): *"all these .MD files ... are in the repository,
/// right? There's nothing locked locally, right?"* — which is exactly the kind of question a test
/// should be answering instead.
///
/// It derives the role list from the SPAWNER rather than from a list written here: a hard-coded
/// list is one more copy to drift, and this file exists because of drift.
/// </summary>
public class EverySpawnableRoleIsInTheKitTests
{
    /// <summary>Below this the scan is not reading the spawner and can prove nothing.</summary>
    const int PLAUSIBLE_ROLE_FLOOR = 3;

    [Fact]
    public void EveryRoleTheAppLaunchesHasItsCommandFileInTheRepo()
    {
        var spawner = Read_RepoFile(Path.Combine("AIOrchestratorCoreLib", "Spawning", "SpawnCommand_Builder.cs"));

        // Each launch reads: ... '/supervisor {orchId}' ... — the role is the word after the slash.
        var roles = Regex.Matches(spawner, @"'/(?<role>[a-z-]+) ")
            .Select(match => match.Groups["role"].Value)
            .Distinct()
            .ToList();

        // THE HARNESS PROVES ITSELF FIRST: every assertion below is about presence, and a scan that
        // found no roles at all would pass in silence — the exact shape of the bug it guards.
        Assert.True(
            roles.Count >= PLAUSIBLE_ROLE_FLOOR,
            $"found {roles.Count} spawnable roles — the scan is not reading SpawnCommand_Builder");

        Assert.Contains("communicator", roles);

        foreach (var role in roles)
        {
            var path = Find_RepoPath(Path.Combine("kit", "commands", $"{role}.md"));

            Assert.True(
                path != null,
                $"the app spawns '/{role}' but kit/commands/{role}.md is not in the repo — it exists only on whichever machine happens to have it, and a fresh install boots that session on an unknown command");
        }
    }

    static string Read_RepoFile(string relativePath)
    {
        var path = Find_RepoPath(relativePath);

        Assert.True(path != null, $"{relativePath} was not found walking up from {AppContext.BaseDirectory}");

        return File.ReadAllText(path!);
    }

    static string? Find_RepoPath(string relativePath)
    {
        var folder = AppContext.BaseDirectory;

        for (var depth = 0; depth < 8; depth++)
        {
            var candidate = Path.Combine(folder, relativePath);

            if (File.Exists(candidate))
                return candidate;

            var parent = Directory.GetParent(folder);

            if (parent == null)
                break;

            folder = parent.FullName;
        }

        return null;
    }
}
