using AIOrchestratorCoreLib.Sessions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Sessions;

/// <summary>
/// The seed is written into EVERY member channel, reviewers included, and it names the author word
/// the member should sign with. It said `supervisor|implementer` in a reviewer's channel — telling
/// that reviewer to sign as an implementer.
///
/// Nothing has gone wrong yet: reviewers write `FROM reviewer` because their role command says so,
/// and zero occurrences turned up across 17 reviewer channels. The author word stopped being
/// cosmetic though — window markers are read only from member-authored entries and a reviewer is
/// specifically barred from that state — so a file instructing the wrong word is a defect waiting
/// for a session that trusts the file it was handed over the command it was given.
/// </summary>
public class ChannelSeedBuilderTests
{
    [Fact]
    public void AReviewerChannelTellsItToSignAsAReviewer()
    {
        var seed = ChannelSeed_Builder.Build_ImplementerChannelSeed("orch-1", "rev-1");

        Assert.Contains("FROM supervisor|reviewer", seed);
        Assert.DoesNotContain("implementer —", seed);
    }

    [Fact]
    public void AnImplementerChannelIsUnchanged()
    {
        var seed = ChannelSeed_Builder.Build_ImplementerChannelSeed("orch-1", "imp-2");

        Assert.Contains("FROM supervisor|implementer", seed);
    }

    /// <summary>Two-digit ids resolve by kind, not by a prefix someone eyeballed.</summary>
    [Fact]
    public void TheKindIsResolvedForTwoDigitIds()
    {
        Assert.Contains("FROM supervisor|reviewer", ChannelSeed_Builder.Build_ImplementerChannelSeed("orch-1", "rev-10"));
        Assert.Contains("FROM supervisor|implementer", ChannelSeed_Builder.Build_ImplementerChannelSeed("orch-1", "imp-10"));
    }

    [Fact]
    public void TheSeedNamesTheMemberAndTheOrchestration()
    {
        var seed = ChannelSeed_Builder.Build_ImplementerChannelSeed("crm-invoice-3", "rev-1");

        Assert.Contains("crm-invoice-3", seed);
        Assert.Contains("rev-1", seed);
    }
}
