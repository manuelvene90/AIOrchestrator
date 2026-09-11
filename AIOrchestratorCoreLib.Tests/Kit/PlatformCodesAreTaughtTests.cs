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

    /// <summary>
    /// The role protocols moved to kit/skills/&lt;role&gt;/SKILL.md when the kit became a plugin. The
    /// argument is still the old "&lt;role&gt;.md" spelling, because it is what every assertion above
    /// reads as; only where the file LIVES changed. Refuses rather than returning a guess — a
    /// content test that located no content passes by finding nothing (decision 20).
    /// </summary>
    static string Read_RoleCommand(string fileName)
    {
        var role = Path.GetFileNameWithoutExtension(fileName);

        return Kit.KitRepoFiles.Find_RoleProtocol(role) is string path
            ? File.ReadAllText(path)
            : throw new Exception($"kit/skills/{role}/SKILL.md was not found walking up from {AppContext.BaseDirectory} — REFUSING to assert about a file this harness never read.");
    }
}
