using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.Siblings;

/// <summary>
/// A SIBLING SENDS THE OWNER A FILE FROM ITS OWN WORKTREE (final review I2, 2026-09-24). The attachment gate
/// allowed the orchestration's repository, its channel folder and <c>~/mockups</c> — and a sibling works in
/// a worktree the skill puts at <c>../&lt;repo&gt;.worktrees/…</c>, outside all three, so "send me the build
/// log" was refused with "a file you sent the owner was NOT delivered". The gate now reads
/// <c>WorkingPath_Resolver.Resolve</c>, the one definition of where a session runs (decision 12), and
/// nothing wider: a path outside both the repository and the worktree is still refused.
/// </summary>
public class SiblingOwnerFileTests : IDisposable
{
    const string REFUSED_SUBJECT = "a file you sent the owner was NOT delivered";

    readonly SiblingEngine_Harness _harness = new();

    public void Dispose()
    {
        _harness.Dispose();
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AFileFromTheSiblingsOwnWorktree_IsDelivered()
    {
        var sibling = await Start_LinkedSibling_Async();
        var file = Path.Combine(_harness.WorktreePath, "sib-final-build.log");
        File.WriteAllText(file, "build ok\n");

        _harness.Append_Solo(sibling, "the build log you asked for", $"{OwnerPush_Policy.ATTACH_MARKER} {file}");

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => _harness.Telegram.Documents.Any(document => document.FileName == "sib-final-build.log"), 20_000),
            $"the worktree file never reached the phone.{Environment.NewLine}{_harness.Channel(sibling)}{Environment.NewLine}{_harness.Log.Dump()}");

        Assert.DoesNotContain(REFUSED_SUBJECT, _harness.Channel(sibling), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AFileOutsideTheRepoAndTheWorktree_IsStillRefused()
    {
        var sibling = await Start_LinkedSibling_Async();

        // Beside the repository and the worktree, inside neither — and not the channel folder or ~/mockups.
        var file = Path.Combine(Path.GetDirectoryName(_harness.RepoPath)!, "sib-final-outside.log");
        File.WriteAllText(file, "not yours to send\n");

        _harness.Append_Solo(sibling, "a log from somewhere else", $"{OwnerPush_Policy.ATTACH_MARKER} {file}");

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => _harness.Channel(sibling).Contains(REFUSED_SUBJECT, StringComparison.Ordinal), 20_000),
            $"a file outside both roots was not refused.{Environment.NewLine}{_harness.Channel(sibling)}{Environment.NewLine}{_harness.Log.Dump()}");

        Assert.DoesNotContain(_harness.Telegram.Documents.ToList(), document => document.FileName == "sib-final-outside.log");
    }

    async Task<string> Start_LinkedSibling_Async()
    {
        var parent = await _harness.Start_Solo_Async("AI-Orch · settings work");
        var sibling = await _harness.Start_Solo_Async("AI-Orch · limits work");

        _harness.Store.Set_EndeavourId(parent, parent);
        _harness.Store.Set_SiblingLink(sibling, parent, parent, $"{parent}#1", _harness.WorktreePath);

        Assert.False(
            _harness.WorktreePath.StartsWith(_harness.RepoPath, StringComparison.OrdinalIgnoreCase),
            "the fixture's worktree sits inside the repository, so this test could not tell the two roots apart");

        return sibling;
    }
}
