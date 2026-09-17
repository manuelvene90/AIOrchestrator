using AIOrchestratorCoreLib.Channels;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE SAME FOUR RULES, ASKED FROM OUTSIDE THE DISPATCHER. These cases restate what
/// <see cref="WakeUpDigestReviewFixTests"/> already pins through the dispatcher — deliberately, because
/// the extraction is only correct if both callers get the same answers. If one of these disagrees with
/// its dispatcher twin, the extraction changed behaviour and the extraction is wrong.
/// </summary>
public class WakeDecisionResolverTests
{
    // ONE stamp for the whole wake family, on the fixture that writes the entries with it.
    static readonly DateTime T0 = WakeFixture.T0;

    [Fact]
    public void The_owner_wakes_it_now()
    {
        using var fixture = WakeFixture.ForSupervisor("repo-1").With_OwnerEntry("do the merge");

        var decision = fixture.Resolve(digestHeldSince: null, nowLocal: T0);

        Assert.NotNull(decision);
        Assert.Contains("the owner wrote", decision!.Reason);
    }

    [Fact]
    public void An_ordinary_member_report_is_held_for_the_digest()
    {
        using var fixture = WakeFixture.ForSupervisor("repo-1").With_SeenMemberEntry("imp-1", "TASK 1 committed abc1234");

        var decision = fixture.Resolve(digestHeldSince: T0, nowLocal: T0.AddMinutes(1));

        Assert.Null(decision);
    }

    [Fact]
    public void The_same_report_wakes_it_once_the_window_has_passed()
    {
        using var fixture = WakeFixture.ForSupervisor("repo-1").With_SeenMemberEntry("imp-1", "TASK 1 committed abc1234");

        var decision = fixture.Resolve(digestHeldSince: T0, nowLocal: T0.AddMinutes(6));

        Assert.NotNull(decision);

        // NOT "because six minutes passed" — that is true of the held case too until the window is up.
        // The reason is the ONE thing that distinguishes the two calls, so it is what is asserted.
        Assert.Contains("digest elapsed", decision!.Reason);
    }

    [Fact]
    public void An_app_note_never_produces_a_decision_and_rides_the_next_one()
    {
        using var fixture = WakeFixture.ForSupervisor("repo-1").With_AppNote("PLAN.md is behind your verdicts", T0);

        Assert.Null(fixture.Resolve(digestHeldSince: null, nowLocal: T0.AddMinutes(1)));

        fixture.With_OwnerEntry("status?");
        var decision = fixture.Resolve(digestHeldSince: null, nowLocal: T0.AddMinutes(2));

        Assert.NotNull(decision);
        Assert.Contains(decision!.Pending, p => p.Entry.Author == ChannelAuthors.App);

        // THE NOTE RIDES IN FRONT AND DECIDED NOTHING — the two halves of the rule, and a set that
        // merely CONTAINS the note would pass with either of them broken.
        Assert.Equal(ChannelAuthors.App, decision.Pending[0].Entry.Author);
        Assert.Contains("the owner wrote", decision.Reason);
    }
}
