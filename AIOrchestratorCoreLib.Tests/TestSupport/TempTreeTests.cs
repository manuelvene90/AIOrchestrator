using Xunit;

namespace AIOrchestratorCoreLib.Tests.TestSupport;

/// <summary>
/// The teardown helper is itself test infrastructure, so it gets probed: a helper that silently
/// did nothing would leak every fixture's temp tree and nobody would notice — the same shape as
/// CLAUDE.md decision 20's harness that certifies without running.
/// </summary>
public sealed class TempTreeTests
{
    /// <summary>It really deletes — swallowing failures must not become swallowing the work.</summary>
    [Fact]
    public void APopulatedTree_IsGone_Afterwards()
    {
        var root = Path.Combine(Path.GetTempPath(), "aiorch-temptree-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "a", "b"));
        File.WriteAllText(Path.Combine(root, "a", "b", "leaf.txt"), "x");
        File.WriteAllText(Path.Combine(root, "top.txt"), "y");

        TempTree.Delete_BestEffort(root);

        Assert.False(Directory.Exists(root));
    }

    /// <summary>A fixture that never got as far as creating its root still disposes cleanly.</summary>
    [Fact]
    public void APathThatWasNeverThere_ThrowsNothing()
    {
        var missing = Path.Combine(Path.GetTempPath(), "aiorch-temptree-absent-" + Guid.NewGuid().ToString("N"));

        TempTree.Delete_BestEffort(missing);

        Assert.False(Directory.Exists(missing));
    }

    /// <summary>
    /// The point of the helper: a delete that cannot finish must not throw out of Dispose. An open
    /// read handle is the portable stand-in for the APFS race — on Windows it locks the file
    /// outright, on macOS the delete is permitted, so the assertion is only that nothing escapes.
    /// </summary>
    [Fact]
    public void ATreeItCannotFullyRemove_StillThrowsNothing()
    {
        var root = Path.Combine(Path.GetTempPath(), "aiorch-temptree-held-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var held = Path.Combine(root, "held.txt");
        File.WriteAllText(held, "z");

        using (var _ = new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            TempTree.Delete_BestEffort(root);
        }

        TempTree.Delete_BestEffort(root);
    }
}
