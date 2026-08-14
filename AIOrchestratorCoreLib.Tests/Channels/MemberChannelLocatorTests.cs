using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// A SOLO HAS NO SPOKE — it writes on the owner channel, because in a basic orchestration it IS the
/// conversation with the owner. Every reader that built a member's channel path from its member id
/// therefore pointed at `solo-1/channel.md`: a file the store seeds at creation and nothing ever
/// writes to again.
///
/// The worst shape a wrong answer can take, because nothing errors. The owner was shown
/// "solo-1: new — no traffic · last wrote 5 h 31 min ago" while they were mid-conversation with that
/// very session, and the mtime being quoted was the moment the seed file was created.
///
/// And it was not only cosmetic: `/resume` and the respawn notice APPEND to this path, so on a basic
/// orchestration the owner's "wake everything up" reached every session except the only one there was.
/// </summary>
public class MemberChannelLocatorTests
{
    static readonly ISupervisionPaths PATHS = SupervisionPaths_Factory.Create(@"C:\sup");

    [Fact]
    public void ASolo_ReadsAndWritesTheOwnerChannel_NotTheDeadSeedFileInItsFolder()
    {
        Assert.Equal(
            PATHS.Get_OwnerChannelFile("arb-fix"),
            MemberChannel_Locator.Get_ChannelFile(PATHS, "arb-fix", "solo-1"));
    }

    [Theory]
    [InlineData("imp-1")]
    [InlineData("imp-12")]
    [InlineData("rev-1")]
    public void EveryOtherKind_KeepsItsOwnSpoke(string memberId)
    {
        Assert.Equal(
            PATHS.Get_ImplementerChannelFile("arb-fix", memberId),
            MemberChannel_Locator.Get_ChannelFile(PATHS, "arb-fix", memberId));
    }

    /// <summary>
    /// The two paths must genuinely differ, or the test above would pass against a locator that does
    /// nothing at all — the fixture is what makes the solo case meaningful.
    /// </summary>
    [Fact]
    public void TheTwoLocationsAreNotTheSameFile()
    {
        Assert.NotEqual(
            PATHS.Get_OwnerChannelFile("arb-fix"),
            PATHS.Get_ImplementerChannelFile("arb-fix", "solo-1"));
    }
}
