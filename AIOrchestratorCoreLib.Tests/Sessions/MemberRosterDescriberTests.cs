using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationMember;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Sessions;

/// <summary>
/// rev-5 F8: a basic orchestration's card said "1 implementer" for something with no implementer and
/// no supervisor, because everything that was not a reviewer fell into the implementer bucket.
///
/// The argument for fixing it is one this code already made: reviewers were broken out of that count
/// precisely because a hidden count "lies about where the spend is going". A solo hidden inside the
/// implementer count is the same lie, on the surface the owner reads before deciding whether to spend
/// more — and it survived because nothing in the app project has any concept of a solo.
///
/// Moved into CoreLib to be testable at all. The WPF project has no suite, and this is a counting
/// rule rather than a layout.
/// </summary>
public class MemberRosterDescriberTests
{
    /// <summary>
    /// A BASIC ORCHESTRATION SAYS WHAT IT IS. It is the whole orchestration talking to the owner, so
    /// it is not counted beside roles that do not exist there — "1 solo · 0 implementers" would be
    /// true and would still invite the reader to look for the rest of a crew.
    /// </summary>
    [Fact]
    public void ASoloIsNotCountedAsAnImplementer()
    {
        var text = MemberRoster_Describer.Describe_OpenMembers([Member("solo-1")]);

        Assert.Equal("one solo session", text);
        Assert.DoesNotContain("implementer", text);
    }

    /// <summary>An ordinary crew is unchanged — the wording the owner already knows.</summary>
    [Fact]
    public void ACrewReadsAsBefore()
    {
        Assert.Equal(
            "2 implementers · 1 reviewer",
            MemberRoster_Describer.Describe_OpenMembers([Member("imp-1"), Member("imp-2"), Member("rev-1")]));

        Assert.Equal("1 implementer", MemberRoster_Describer.Describe_OpenMembers([Member("imp-1")]));
    }

    /// <summary>
    /// A CLOSED member counts for nothing, including a closed solo — which is exactly the state a
    /// promoted orchestration is in, and the one that would otherwise report a solo for ever.
    /// </summary>
    [Fact]
    public void APromotedOrchestrationReadsAsACrew()
    {
        var text = MemberRoster_Describer.Describe_OpenMembers([Member("solo-1", closed: true), Member("imp-1")]);

        Assert.Equal("1 implementer", text);
        Assert.DoesNotContain("solo", text);
    }

    /// <summary>
    /// And the half-promoted state is described honestly rather than hidden: a live solo BESIDE a
    /// crew is a real state — it is what a failed spawn leaves — and the owner should see both.
    /// </summary>
    [Fact]
    public void AHalfPromotedOrchestrationShowsBoth()
    {
        var text = MemberRoster_Describer.Describe_OpenMembers([Member("solo-1"), Member("imp-1")]);

        Assert.Contains("1 solo", text);
        Assert.Contains("1 implementer", text);
    }

    /// <summary>An orchestration with nothing open says so rather than counting a phantom.</summary>
    [Fact]
    public void NothingOpenReadsAsZeroImplementers()
    {
        Assert.Equal("0 implementers", MemberRoster_Describer.Describe_OpenMembers([Member("imp-1", closed: true)]));
    }

    static IOrchestrationMember Member(string memberId, bool closed = false)
    {
        return OrchestrationMember_Factory.Create(memberId, 1234, DateTime.UtcNow, closed ? DateTime.UtcNow : null);
    }
}
