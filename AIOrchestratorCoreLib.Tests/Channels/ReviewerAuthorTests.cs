using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.DiscoveredChannel;
using AIOrchestratorCoreLib.Mirroring;
using AIOrchestratorCoreLib.Status;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// A reviewer writes `## [n] FROM reviewer — …`, which parsed as Unknown until this was fixed — so
/// its entries were invisible to the mirror, to state resolution and to the idle detector, and the
/// detector then treated a reviewer's OWN entry as unread traffic and nudged it about it.
/// Everything meaning "a member spoke" must go through Is_Member, never a comparison to Implementer.
/// </summary>
public class ReviewerAuthorTests
{
    [Fact]
    public void Reviewer_IsParsedAsItsOwnAuthor_NotUnknown()
    {
        var entries = ChannelEntry_Parser.Parse_All("## [1] FROM reviewer — 2026-08-07 14:00 — rev-1 online");

        Assert.Equal(ChannelAuthors.Reviewer, Assert.Single(entries).Author);
    }

    [Theory]
    [InlineData(ChannelAuthors.Implementer, true)]
    [InlineData(ChannelAuthors.Reviewer, true)]
    [InlineData(ChannelAuthors.Supervisor, false)]
    [InlineData(ChannelAuthors.Owner, false)]
    [InlineData(ChannelAuthors.App, false)]
    [InlineData(ChannelAuthors.Communicator, false)]
    [InlineData(ChannelAuthors.Unknown, false)]
    public void Is_Member_CoversExactlyTheSpokeOwners(ChannelAuthors author, bool expected)
    {
        Assert.Equal(expected, ChannelAuthor_Kinds.Is_Member(author));
    }

    [Fact]
    public void ReviewerPresenceLine_Mirrors_InPurple_NotImplementerBlue()
    {
        var channel = DiscoveredChannel_Factory.Create_ForImplementer("orch-1", "rev-1", "path/rev-1/channel.md");
        var entry = Assert.Single(ChannelEntry_Parser.Parse_All("## [1] FROM reviewer — 2026-08-07 14:00 — rev-1 online"));

        Assert.True(MirrorText_Formatter.Should_Mirror(channel, entry));
        Assert.Equal("🟣 rev-1: online", MirrorText_Formatter.Format(channel, entry));
    }

    [Fact]
    public void ImplementerPresenceLine_KeepsItsBlue()
    {
        var channel = DiscoveredChannel_Factory.Create_ForImplementer("orch-1", "imp-2", "path/imp-2/channel.md");
        var entry = Assert.Single(ChannelEntry_Parser.Parse_All("## [1] FROM implementer — 2026-08-07 14:00 — imp-2 online"));

        Assert.Equal("🔵 imp-2: online", MirrorText_Formatter.Format(channel, entry));
    }

    /// <summary>
    /// The harmful consequence of the old behaviour: a reviewer that had just filed its report
    /// looked like a member with unanswered traffic, so the idle detector would nudge it about its
    /// own entry. State resolution must see reviewer entries as the member speaking.
    /// </summary>
    [Fact]
    public void ReviewerReport_ResolvesAsAwaitingReview_LikeAnImplementerReport()
    {
        var entries = ChannelEntry_Parser.Parse_All("""
        ## [1] FROM supervisor — 2026-08-07 14:00 — review the order path, depth deep
        ## [2] FROM reviewer — 2026-08-07 14:40 — review of the order path — depth deep — 2 findings
        """);

        Assert.Equal(MemberStates.AwaitingSupervisorReview, MemberState_Resolver.Resolve(entries));
    }
}
