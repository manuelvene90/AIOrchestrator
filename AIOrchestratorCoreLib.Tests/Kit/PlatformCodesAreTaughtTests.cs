using AIOrchestratorCoreLib.Sessions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// THE CODES ARE DEFINED ONCE AND TAUGHT EVERYWHERE, which is the shape PlanLedger_Markers had to
/// be rebuilt into after its list drifted in two of its five copies.
///
/// These live in three role commands — the general supervisor RESOLVES a code the owner speaks, and
/// the supervisor and solo NAME their topic with it — so the same list exists in four places the
/// moment it exists at all. This is the guard that keeps them one list.
/// </summary>
public class PlatformCodesAreTaughtTests
{
    /// <summary>The roles that either resolve a spoken code or write one into a topic name.</summary>
    static readonly string[] MUST_TEACH = ["general-supervisor.md", "supervisor.md", "solo.md"];

    [Fact]
    public void EveryCodeIsTaughtByEveryRoleThatNeedsIt()
    {
        Assert.NotEmpty(Platform_Abbreviations.ALL);

        foreach (var fileName in MUST_TEACH)
        {
            var text = Read_RoleCommand(fileName);

            foreach (var (code, platform, _) in Platform_Abbreviations.ALL)
            {
                Assert.True(
                    text.Contains($"`{code}`"),
                    $"{fileName} does not teach the code `{code}` ({platform}) — a role that cannot read a code the owner speaks will guess a repo");
            }
        }
    }

    /// <summary>
    /// A SUB-PRODUCT RESOLVES TO ITS PARENT'S REPO AND KEEPS ITS OWN NAME — the owner's
    /// clarification, and the half a reader is most likely to collapse.
    /// </summary>
    [Fact]
    public void ASubProductResolvesToItsParentRepo()
    {
        Assert.Equal("SL", Platform_Abbreviations.Resolve_RepoCode_OrNull("IS"));
        Assert.Equal("SL", Platform_Abbreviations.Resolve_RepoCode_OrNull("PB"));

        // A top-level code resolves to itself.
        Assert.Equal("AI-Orch", Platform_Abbreviations.Resolve_RepoCode_OrNull("AI-Orch"));

        // Case is the owner typing on a phone, not a different platform.
        Assert.Equal("SL", Platform_Abbreviations.Resolve_RepoCode_OrNull("is"));
    }

    /// <summary>
    /// An unknown code answers NULL rather than a guess. Starting an orchestration on the wrong repo
    /// costs a session and a worktree to discover, so this is the one place a shrug beats a default.
    /// </summary>
    [Fact]
    public void AnUnknownCodeIsNotGuessed()
    {
        Assert.Null(Platform_Abbreviations.Resolve_RepoCode_OrNull("SA"));
        Assert.Null(Platform_Abbreviations.Resolve_RepoCode_OrNull(""));
    }

    static string Read_RoleCommand(string fileName)
    {
        var folder = AppContext.BaseDirectory;

        for (var depth = 0; depth < 8; depth++)
        {
            var candidate = Path.Combine(folder, "kit", "commands", fileName);

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            var parent = Directory.GetParent(folder);

            if (parent == null)
                break;

            folder = parent.FullName;
        }

        Assert.Fail($"kit/commands/{fileName} was not found walking up from {AppContext.BaseDirectory}");

        return "";
    }
}
