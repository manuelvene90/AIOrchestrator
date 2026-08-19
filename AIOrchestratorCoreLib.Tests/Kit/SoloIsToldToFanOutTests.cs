using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// A SOLO IS THE WHOLE TEAM, so sequential work it could have parallelised is wall-clock the owner
/// pays for nothing. They raised it on 2026-08-19 — *"solo sessions never use parallel sub agents,
/// they should obviously do so to speed things up"* — and they were right: nothing in the app, the
/// settings or the kit forbade it, and it still was not happening.
///
/// The rule WAS in solo.md, as a cross-reference to implementer.md. A cross-reference loses to
/// whatever default a session arrives with, which is why this pins the instruction being present IN
/// solo.md and stated as a default rather than as a permission.
///
/// Honest about its own strength: this proves the file SAYS it. Nothing in a test — or in the app,
/// which enforces at the point of effect everywhere it can — can make a session actually call the
/// tool. That limit is the finding, not an oversight.
/// </summary>
public class SoloIsToldToFanOutTests
{
    [Fact]
    public void SoloIsToldFanOutIsItsDefaultRatherThanAnOption()
    {
        var solo = Find_RoleCommandFile("solo.md");

        Assert.Contains("your DEFAULT, not an option", solo);

        // The trigger has to be IN solo.md: a solo that must follow a cross-reference to learn its
        // own default is the shape that failed.
        Assert.Contains("two or more independent read-only pieces", solo);
        Assert.Contains("in PARALLEL", solo);
    }

    /// <summary>
    /// The guard rails travel WITH the encouragement, in the same file. Telling a session to fan out
    /// harder without repeating the disjoint-files rule would trade slow work for corrupted work.
    /// </summary>
    [Fact]
    public void TheWritersRuleTravelsWithIt()
    {
        var solo = Find_RoleCommandFile("solo.md");

        Assert.Contains("DISJOINT file sets", solo);
        Assert.Contains("NOT evidence", solo);
    }

    static string Find_RoleCommandFile(string fileName)
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

        // A harness that cannot find its subject fails loudly rather than certifying an absence.
        Assert.Fail($"kit/commands/{fileName} was not found walking up from {AppContext.BaseDirectory}");

        return "";
    }
}
