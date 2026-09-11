using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// THE ONE RISK SPLITTING A PROTOCOL INTRODUCES: a rule that ends up in a file the session never
/// opens is not a rule any more. It is the exact failure the 2026-09-06 live run recorded from the
/// other direction — a paragraph conditioned on something the model could not see was skipped, and
/// the session hung until its turn timed out.
///
/// So every reference file must be NAMED by the protocol that owns it, and named imperatively. This
/// cannot check that a model obeys; it can and does check that the instruction to read the file
/// exists at all, which is the half that a refactor can silently drop.
/// </summary>
public class NoProtocolFileIsOrphanedTests
{
    [Fact]
    public void EveryReferenceFile_IsNamedByTheProtocolThatOwnsIt()
    {
        var protocols = KitRepoFiles.Find_AllRoleProtocols();
        Assert.NotEmpty(protocols);

        var checkedFiles = 0;

        foreach (var (role, path) in protocols)
        {
            var referenceFolder = Path.Combine(Path.GetDirectoryName(path)!, "reference");

            if (!Directory.Exists(referenceFolder))
                continue;

            var protocol = File.ReadAllText(path);

            foreach (var reference in Directory.GetFiles(referenceFolder, "*.md"))
            {
                var name = Path.GetFileName(reference);
                checkedFiles++;

                Assert.True(
                    protocol.Contains($"reference/{name}", StringComparison.Ordinal),
                    $"kit/skills/{role}/reference/{name} exists but {role}'s SKILL.md never tells the session to read it — every rule in it is dead text");
            }
        }

        // Asserted so that a refactor which deletes every reference file cannot make this pass by
        // leaving nothing to check. Two per role, plus the supervisor's stream-runner.md.
        Assert.Equal(13, checkedFiles);
    }

    /// <summary>
    /// Naming the file is not enough — "see also" is how a rule becomes optional. Each pointer says
    /// READ, and says WHEN.
    /// </summary>
    [Fact]
    public void EveryPointer_TellsTheSessionToReadItAndWhen()
    {
        foreach (var (role, path) in KitRepoFiles.Find_AllRoleProtocols())
        {
            var protocol = File.ReadAllText(path);

            Assert.Contains("READ `reference/watcher.md` NOW", protocol);
            Assert.Contains("READ `reference/print-runner.md`", protocol);
        }
    }
}
