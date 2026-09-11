using AIOrchestratorCoreLib.Kit;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// THE PROPERTY TWO RETIRED INSTALLER TESTS EXISTED FOR: an instruction the kit gives a session must
/// not point at a path that does not exist. It used to be checked by asserting that the installer
/// COPIED the hooks and the append helper into ~/.claude. Nothing copies them now — they ship in the
/// plugin — so the same property is asserted where it moved to: what the role frontmatter NAMES must
/// be in the shipped kit, and the helper every role invokes must be on the plugin's PATH.
///
/// Both refuse to run when they cannot find the kit. A pass from a test that located no files is a
/// pass about nothing (decision 20).
/// </summary>
public class RoleHooksAreShippedTests
{
    [Fact]
    public void EveryHookARoleDeclares_IsShippedInTheKit()
    {
        var protocols = KitRepoFiles.Find_AllRoleProtocols();
        Assert.NotEmpty(protocols);

        var hooksFolder = KitRepoFiles.Find(Path.Combine("kit", "hooks"))
            ?? throw new Exception("kit/hooks was not found — REFUSING to pass about hooks this test never looked at.");

        var declared = 0;

        foreach (var (role, path) in protocols)
        {
            foreach (var line in File.ReadLines(path))
            {
                var marker = "${CLAUDE_PLUGIN_ROOT}/hooks/";
                var start = line.IndexOf(marker, StringComparison.Ordinal);

                if (start < 0)
                    continue;

                var name = line[(start + marker.Length)..].TrimEnd('"', ' ');
                declared++;

                Assert.True(
                    File.Exists(Path.Combine(hooksFolder, name)),
                    $"{role} declares the hook '{name}' but kit/hooks/{name} is not shipped — the declaration is a dead path");
            }
        }

        // The count is asserted so that a frontmatter someone empties cannot make this pass by
        // having nothing left to check. It was 6 until stage 4g wired the soft-boundary advisory
        // into the three roles that do the work — implementer, reviewer and solo (SoftBoundaryHookTests).
        Assert.Equal(9, declared);
    }

    /// <summary>
    /// The append helper is what every role is told to write channels with. It ships in the plugin's
    /// bin/, which Claude Code prepends to the Bash tool's PATH (measured) — which is why the roles
    /// now call it by bare name. If it were not shipped there, every one of those instructions would
    /// be a dead path and sessions would fall back to the unlocked appends it exists to replace.
    /// </summary>
    [Fact]
    public void TheAppendHelper_IsShippedOnThePluginsPath_AndTheRolesCallItByBareName()
    {
        Assert.NotNull(KitRepoFiles.Find(Path.Combine("kit", "bin", "channel-append.sh")));

        var callers = 0;

        foreach (var (role, path) in KitRepoFiles.Find_AllRoleProtocols())
        {
            var text = File.ReadAllText(path);

            Assert.DoesNotContain(".claude/commands/channel-append.sh", text);

            if (text.Contains("channel-append.sh \\", StringComparison.Ordinal))
                callers++;
        }

        Assert.Equal(5, callers);
    }

    /// <summary>
    /// THE BIT THAT MAKES bin/ MEAN ANYTHING. The plugin's bin/ is prepended to the Bash tool's
    /// PATH, which is why every role now calls the helper by bare name — but PATH resolution only
    /// finds an EXECUTABLE file, and the helper was committed 100644 when it moved there.
    ///
    /// Measured 2026-09-06, in a live stream turn: `which channel-append.sh` printed
    /// "NOT FOUND IN PATH", and the supervisor went on to append to its own channel by hand,
    /// annotating it "(Entry written without lock because channel-append.sh is not installed)" —
    /// the exact unlocked write the helper exists to replace, on the busiest channel in the system.
    ///
    /// Skipped where the mode does not exist rather than asserted falsely; the git index carries
    /// 100755, so a checkout on any OS ships it executable.
    /// </summary>
    [Fact]
    public void TheAppendHelper_IsExecutable_OrBinOnPathBuysNothing()
    {
        var helper = KitRepoFiles.Find(Path.Combine("kit", "bin", "channel-append.sh"))
            ?? throw new Exception("kit/bin/channel-append.sh was not found — REFUSING to pass about a file this test never located.");

        if (OperatingSystem.IsWindows())
            return;

        Assert.True(
            File.GetUnixFileMode(helper).HasFlag(UnixFileMode.UserExecute),
            $"{helper} is not executable, so `channel-append.sh` cannot resolve on PATH and every role's instruction to call it by bare name is dead");
    }

    /// <summary>
    /// A role is entered because the app spawned it. Without this flag the six descriptions sit in
    /// every session's context and a model can decide to become a supervisor on its own.
    /// </summary>
    [Fact]
    public void NoRole_CanBeEnteredByAModelDeciding_ToEnterIt()
    {
        var protocols = KitRepoFiles.Find_AllRoleProtocols();
        Assert.Equal(6, protocols.Count);

        foreach (var (role, path) in protocols)
            Assert.Contains("disable-model-invocation: true", File.ReadAllText(path));
    }

    /// <summary>
    /// A house skill is the opposite of a role: a session loads it by choice, so the flag that keeps a
    /// model from entering a role must be OFF on it — and it must not be mistaken for a role either.
    /// Pinned the day the first one (`subagents`, stage 1f) landed and was counted as a seventh role.
    /// </summary>
    [Fact]
    public void AHouseSkill_IsNotARole_AndAModelMayLoadIt()
    {
        var houseSkills = KitRepoFiles.Find_AllHouseSkills();
        Assert.Contains(houseSkills, entry => entry.Skill == "subagents");

        foreach (var (skill, path) in houseSkills)
            Assert.DoesNotContain("disable-model-invocation: true", File.ReadAllText(path));
    }

    /// <summary>
    /// The slash word a role answers to is the one Running/SessionRoles.cs composes. A skill's `name`
    /// IS that word, so a rename here silently breaks every spawn — measured: a name the CLI does not
    /// know answers "Unknown command" and the session dies on its first prompt.
    /// </summary>
    [Fact]
    public void EveryRoleSkill_IsNamedTheWordTheSpawnerSays()
    {
        foreach (var (role, path) in KitRepoFiles.Find_AllRoleProtocols())
            Assert.Contains($"name: {role}", File.ReadAllText(path));
    }
}
