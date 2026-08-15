using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Status;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// THE STALL ALERT FIRED ON PLAIN SILENCE. The owner got "⚠️ quiet for 28 min and no session is
/// working — text it to wake it up" on two solo topics and answered: "Makes no sense." The channel
/// was quiet because THEY had stopped texting; the session was idle exactly as designed, and being
/// told to wake something that is not asleep is noise the owner cannot act on.
///
/// Their ruling, 2026-08-15: alert "only if I owe you a reply". So the question stopped being how
/// long the channel has been quiet and became whose move it is.
/// </summary>
public class OwnerOwesReplyDeciderTests
{
    static IChannelEntry Entry(int index, ChannelAuthors author)
    {
        return ChannelEntry_Factory.Create(index, author, "2026-08-15 10:00", "subject", "body", "raw");
    }

    [Fact]
    public void TheSessionSpokeLast_TheOwnerOwesAReply()
    {
        Assert.True(OwnerOwesReply_Decider.Decide([Entry(1, ChannelAuthors.Owner), Entry(2, ChannelAuthors.Solo)]));
    }

    [Fact]
    public void TheOwnerSpokeLast_TheyOweNothing()
    {
        Assert.False(OwnerOwesReply_Decider.Decide([Entry(1, ChannelAuthors.Solo), Entry(2, ChannelAuthors.Owner)]));
    }

    /// <summary>A crew's supervisor is the same voice for this purpose — the owner answers both.</summary>
    [Fact]
    public void ASupervisorCountsTheSameWayASoloDoes()
    {
        Assert.True(OwnerOwesReply_Decider.Decide([Entry(1, ChannelAuthors.Owner), Entry(2, ChannelAuthors.Supervisor)]));
    }

    /// <summary>
    /// THE ONE THAT WOULD DISABLE THE FEATURE SILENTLY. App entries — status pushes, nudges, ledger
    /// complaints — land on their own schedule and are the app talking ABOUT the conversation. If one
    /// counted as the last word, the app's own periodic status would suppress every alert, and the
    /// feature would switch itself off with nothing to show for it.
    /// </summary>
    [Fact]
    public void AppEntriesAfterTheSession_DoNotCancelTheDebt()
    {
        Assert.True(OwnerOwesReply_Decider.Decide(
        [
            Entry(1, ChannelAuthors.Owner),
            Entry(2, ChannelAuthors.Solo),
            Entry(3, ChannelAuthors.App),
            Entry(4, ChannelAuthors.App),
        ]));
    }

    /// <summary>And the mirror of it: an app entry after the OWNER must not invent a debt either.</summary>
    [Fact]
    public void AppEntriesAfterTheOwner_DoNotCreateADebt()
    {
        Assert.False(OwnerOwesReply_Decider.Decide(
        [
            Entry(1, ChannelAuthors.Solo),
            Entry(2, ChannelAuthors.Owner),
            Entry(3, ChannelAuthors.App),
        ]));
    }

    [Fact]
    public void NobodyHasSpokenYet_NothingIsOwed()
    {
        Assert.False(OwnerOwesReply_Decider.Decide([]));
        Assert.False(OwnerOwesReply_Decider.Decide([Entry(1, ChannelAuthors.App)]));
    }

    /// <summary>
    /// Only the LAST exchange decides. A channel full of earlier session entries does not keep the
    /// owner permanently in debt once they have answered.
    /// </summary>
    [Fact]
    public void OnlyTheLastWordCounts()
    {
        Assert.False(OwnerOwesReply_Decider.Decide(
        [
            Entry(1, ChannelAuthors.Solo),
            Entry(2, ChannelAuthors.Solo),
            Entry(3, ChannelAuthors.Solo),
            Entry(4, ChannelAuthors.Owner),
        ]));
    }
}
