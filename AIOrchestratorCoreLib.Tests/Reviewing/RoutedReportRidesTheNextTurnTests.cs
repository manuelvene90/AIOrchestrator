using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Reviewing;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Status;
using AIOrchestratorCoreLib.Tests.Running;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Reviewing;

/// <summary>
/// THE APP-NOTE RULE, APPLIED TO ONE MEMBER ENTRY. A fix report the app has already relayed to a
/// reviewer is not news the supervisor must act on now — it will read it beside the re-review
/// findings, on the turn where a verdict is possible. Until then it RIDES: pending, never consumed,
/// never a reason to spend ~1 M input tokens (measured, VPS 6–9 Sep 2026) on a turn that can only say
/// "noted".
///
/// <para>
/// WHAT THESE CASES DO NOT SHOW. They are the wake DECISION, which is the whole of the saving in
/// ticket mode and none of it under <c>watcher</c> — there the bash monitor still wakes the
/// supervisor on the same append, and what the feature buys is the correct brief in the reviewer's
/// channel. Nothing here may be read as the saving already collected.
/// </para>
/// </summary>
public class RoutedReportRidesTheNextTurnTests
{
    static readonly DateTime T0 = WakeFixture.T0;

    [Fact]
    public void AHeldFixReportAloneStartsNoTurn()
    {
        using var fixture = WakeFixture.ForSupervisor("repo-1").With_SeenMemberEntry("imp-1", "F1 fixed", body: "FIXED: def5678");

        Assert.Null(fixture.Decide(ridingOnly: [fixture.IdentityOfLastEntry], digestHeldSince: T0, nowLocal: T0.AddMinutes(30)));
    }

    /// <summary>
    /// AND WITHOUT THE HOLD IT WOULD HAVE — the live control for the case above, in the same fixture.
    /// A silence asserted alone has two routes to it (CLAUDE.md decision 20).
    /// </summary>
    [Fact]
    public void TheSameReportUnheldWakesTheSupervisorOnceTheDigestElapses()
    {
        using var fixture = WakeFixture.ForSupervisor("repo-1").With_SeenMemberEntry("imp-1", "F1 fixed", body: "FIXED: def5678");

        var decision = fixture.Decide(ridingOnly: [], digestHeldSince: T0, nowLocal: T0.AddMinutes(30));

        Assert.NotNull(decision);
        Assert.Contains("digest elapsed", decision!.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// AND AN IDENTITY THAT IS NOT THIS ENTRY'S HOLDS NOTHING — the second half of the control. A
    /// hold that swallowed the whole pending set whenever ANY contract was routed would pass the case
    /// above and fail here.
    /// </summary>
    [Fact]
    public void AnUnrelatedRidingIdentityHoldsNothing()
    {
        using var fixture = WakeFixture.ForSupervisor("repo-1").With_SeenMemberEntry("imp-1", "F1 fixed", body: "FIXED: def5678");

        Assert.NotNull(fixture.Decide(ridingOnly: ["9f2a1c-not-this-entry"], digestHeldSince: T0, nowLocal: T0.AddMinutes(30)));
    }

    /// <summary>
    /// NOTHING IS LOST BY BEING HELD. The re-review lands, the turn starts for THAT, and the held
    /// report is handed over on the same turn — which is the whole point: one turn, both halves.
    /// </summary>
    [Fact]
    public void TheHeldReportRidesTheTurnTheReReviewStarts()
    {
        using var fixture = WakeFixture.ForSupervisor("repo-1").With_SeenMemberEntry("imp-1", "F1 fixed", body: "FIXED: def5678");
        var held = fixture.IdentityOfLastEntry;

        // THE HELD ENTRY IS STILL PENDING AND STILL NOT CONSUMED at the moment the re-review arrives:
        // asked BEFORE, so the case cannot pass by the report having been quietly handed over on some
        // earlier decision.
        Assert.Null(fixture.Decide(ridingOnly: [held], digestHeldSince: T0, nowLocal: T0.AddMinutes(30)));

        fixture.With_SeenMemberEntry("rev-1", $"{MemberState_Resolver.QUESTION_MARKER} none — F1 verified FIXED", body: "one finding, LOW");

        var decision = fixture.Decide(ridingOnly: [held], digestHeldSince: T0, nowLocal: T0.AddMinutes(31));

        Assert.NotNull(decision);

        // THE RE-REVIEW IS WHAT RELEASED IT, and the held report merely rode along — a set that only
        // CONTAINS the report would pass with the hold broken in either direction.
        Assert.Contains("rev-1", decision!.Reason, StringComparison.Ordinal);
        Assert.Contains(decision.Pending, entry => ChannelEntry_Digest.Compute(entry.Entry) == held);
    }

    /// <summary>
    /// THE OWNER IS NEVER HELD BY THIS OR ANYTHING ELSE. One identity is named, and everything else
    /// on every channel keeps its own timing.
    /// </summary>
    [Fact]
    public void AnOwnerMessageWakesItImmediatelyAndBringsTheHeldReportAlong()
    {
        using var fixture = WakeFixture.ForSupervisor("repo-1").With_SeenMemberEntry("imp-1", "F1 fixed", body: "FIXED: def5678");
        var held = fixture.IdentityOfLastEntry;

        fixture.With_OwnerEntry("where are we?");

        var decision = fixture.Decide(ridingOnly: [held], digestHeldSince: T0, nowLocal: T0.AddMinutes(1));

        Assert.NotNull(decision);
        Assert.Contains("the owner wrote", decision!.Reason, StringComparison.Ordinal);
        Assert.Contains(decision.Pending, entry => ChannelEntry_Digest.Compute(entry.Entry) == held);
    }

    /// <summary>
    /// AND THE RESOLVER ITSELF ASKS THE POLICY. Every case above states the riding set outright, so
    /// all of them would pass with <c>Resolve_OrNull</c> never consulting
    /// <see cref="RoutedHold_Policy"/> at all — which is the wiring the whole saving hangs on. This
    /// one goes through the contract file: the control first, then the identical call with a ROUTED
    /// contract on disk.
    /// </summary>
    [Fact]
    public void TheResolverReadsTheContractFileAndHoldsOnIt()
    {
        using var fixture = WakeFixture.ForSupervisor("repo-1").With_SeenMemberEntry("imp-1", "F1 fixed", body: "FIXED: def5678");

        Assert.NotNull(fixture.Resolve(digestHeldSince: T0, nowLocal: T0.AddMinutes(30)));

        fixture.With_RoutedContract("imp-1", "rev-1", fixture.IdentityOfLastEntry);

        Assert.Null(fixture.Resolve(digestHeldSince: T0, nowLocal: T0.AddMinutes(30)));
    }

    /// <summary>
    /// A MEMBER IS NEVER HELD, whatever the contracts say — holding a member would stop the WORK,
    /// which is the objection <c>QuestionHold_Policy</c>'s own comment raises against gating on a
    /// pending question, and it is just as fair here.
    /// </summary>
    [Fact]
    public void RidingOnlyIsEmptyForEveryRoleButTheSupervisor()
    {
        using var fixture = WakeFixture.ForSupervisor("repo-1")
            .With_SeenMemberEntry("imp-1", "F1 fixed", body: "FIXED: def5678")
            .With_SeenMemberEntry("rev-1", "round one", body: "three findings")
            .With_RoutedContract("imp-1", "rev-1", "9f2a1c");

        Assert.Empty(RoutedHold_Policy.Resolve_RidingOnly(fixture.Paths, fixture.StateFor(SessionRoles.Reviewer, "rev-1")));
        Assert.Empty(RoutedHold_Policy.Resolve_RidingOnly(fixture.Paths, fixture.StateFor(SessionRoles.Implementer, "imp-1")));
        Assert.Contains("9f2a1c", RoutedHold_Policy.Resolve_RidingOnly(fixture.Paths, fixture.StateOfTheSupervisor()));
    }

    /// <summary>
    /// AND A CONTRACT THAT IS ONLY DECLARED HOLDS NOTHING. Until the app has actually relayed the
    /// report, the supervisor is the only one who knows the round is open — holding then would hide
    /// a report nobody else has been told about.
    /// </summary>
    [Fact]
    public void ADeclaredButUnroutedContractHoldsNothing()
    {
        using var fixture = WakeFixture.ForSupervisor("repo-1").With_DeclaredContract("imp-1", "rev-1");

        Assert.Empty(RoutedHold_Policy.Resolve_RidingOnly(fixture.Paths, fixture.StateOfTheSupervisor()));
    }
}
