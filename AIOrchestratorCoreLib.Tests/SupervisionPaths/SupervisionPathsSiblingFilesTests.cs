using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.SupervisionPaths;

/// <summary>
/// The three files a linked orchestration carries for its siblings (spec 2026-09-23 §3.3, §3.4).
/// ISupervisionPaths is the single authority for every path under the supervision home, so the
/// watcher, the digest writer and the executor all read these names from one place.
/// </summary>
public class SupervisionPathsSiblingFilesTests : IDisposable
{
    readonly string _root;

    public SupervisionPathsSiblingFilesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"aiorch-siblingfiles-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void TheThreeSiblingFiles_LiveInTheOrchestrationFolderRoot()
    {
        var paths = SupervisionPaths_Factory.Create(_root);

        Assert.Equal(Path.Combine(paths.Get_OrchestrationFolder("a-1"), "sibling-outbox.md"), paths.Get_SiblingOutboxFile("a-1"));
        Assert.Equal(Path.Combine(paths.Get_OrchestrationFolder("a-1"), ".siblings"), paths.Get_SiblingsListFile("a-1"));
        Assert.Equal(Path.Combine(paths.Get_OrchestrationFolder("a-1"), "ENDEAVOUR.md"), paths.Get_EndeavourDigestFile("a-1"));
    }

    /// <summary>
    /// THE OUTBOX IS NOT A CHANNEL THE TAILER KNOWS, and that is the O3 answer obtained with no code
    /// (spec §3.3). ChannelDiscovery enumerates owner-channel.md and imp-*/rev-* spokes only. If a
    /// later change made it glob every *.md in the folder, sibling traffic would start ringing the
    /// owner's phone. Task 12 pins the same fact at the engine.
    /// </summary>
    [Fact]
    public void AnOutboxWithEntries_IsNotDiscoveredAsAChannel()
    {
        var paths = SupervisionPaths_Factory.Create(_root);
        Directory.CreateDirectory(paths.Get_OrchestrationFolder("a-1"));
        File.WriteAllText(paths.Get_SessionFile("a-1"), "{}");
        File.WriteAllText(paths.Get_OwnerChannelFile("a-1"), "# owner channel\n");
        File.WriteAllText(paths.Get_SiblingOutboxFile("a-1"), "## [1] FROM solo — ASK x\n\nbody\n");

        var found = ChannelDiscovery.Find_ChannelFiles(paths);

        Assert.Contains(found, channel => channel.OrchId == "a-1" && channel.IsOwnerChannel);
        Assert.DoesNotContain(found, channel => channel.FilePath.EndsWith("sibling-outbox.md", StringComparison.OrdinalIgnoreCase));
    }
}
