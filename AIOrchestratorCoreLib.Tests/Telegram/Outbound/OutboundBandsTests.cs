using Xunit;
using AIOrchestratorCoreLib.Telegram.Outbound;

namespace AIOrchestratorCoreLib.Tests.Telegram.Outbound;

/// <summary>
/// THE ORDER IS THE OWNER'S DECISION (2026-09-10), and it is pinned as a number rather than left to
/// a comment, because the scheduler compares these values directly: a band renumbered by a later
/// hand would silently invert the priority this whole change exists to establish.
/// </summary>
public class OutboundBandsTests
{
    [Fact]
    public void TheOwnersTapOutranksEverything()
    {
        Assert.True(OutboundBands.Tap < OutboundBands.Conversation);
        Assert.True(OutboundBands.Conversation < OutboundBands.Cosmetic);
    }

    [Fact]
    public void TheBandsAreExactlyThree()
    {
        Assert.Equal(3, Enum.GetValues<OutboundBands>().Length);
    }
}
