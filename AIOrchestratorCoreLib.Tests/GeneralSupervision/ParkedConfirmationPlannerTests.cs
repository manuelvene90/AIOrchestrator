using AIOrchestratorCoreLib.GeneralSupervision;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.GeneralSupervision;

/// <summary>
/// The guards that decide what happens to a request waiting for the owner's tap — now where the suite
/// can ask about them, because the machinery around them cannot be reached at all.
///
/// FOUR PRODUCTION FAILURES LIVE IN THIS ORDERING and each is a case below. They were found in
/// production, one at a time, and the only record of them was a comment beside a `continue`.
/// </summary>
public class ParkedConfirmationPlannerTests
{
    /// <summary>The ordinary case: nobody has asked, so ask.</summary>
    [Fact]
    public void AnUnaskedRequestIsAsked()
    {
        Assert.Equal(
            ParkedConfirmationActions.Ask,
            ParkedConfirmation_Planner.Decide(isBeingResolved: false, isExpired: false, alreadyAsked: false));
    }

    /// <summary>And an asked one is left alone, which is the whole point of remembering it.</summary>
    [Fact]
    public void AnAlreadyAskedRequestIsLeftAlone()
    {
        Assert.Equal(
            ParkedConfirmationActions.None,
            ParkedConfirmation_Planner.Decide(isBeingResolved: false, isExpired: false, alreadyAsked: true));
    }

    /// <summary>
    /// BEING RESOLVED BEATS EVERYTHING, and this is the failure that cost the most. A request the tap
    /// handler is part-way through has already had its registrations dropped — they go the instant the
    /// owner taps — while its file survives two awaited Telegram calls longer. So anything reading the
    /// file alone sees a request that looks unasked, and the tick posted a SECOND prompt with fresh
    /// buttons for a decision the owner had just made.
    ///
    /// The duplicates then pointed at an archived path that only the file scan could clear, so nothing
    /// ever cleared them — and a tap on that immortal button closed an orchestration the owner had
    /// explicitly refused to close.
    ///
    /// Asserted against BOTH other flags set, so it cannot pass by one of the later guards happening
    /// to fire first.
    /// </summary>
    [Fact]
    public void ARequestBeingResolvedIsUntouchedWhateverElseIsTrue()
    {
        Assert.Equal(
            ParkedConfirmationActions.None,
            ParkedConfirmation_Planner.Decide(isBeingResolved: true, isExpired: true, alreadyAsked: false));

        Assert.Equal(
            ParkedConfirmationActions.None,
            ParkedConfirmation_Planner.Decide(isBeingResolved: true, isExpired: false, alreadyAsked: true));
    }

    /// <summary>
    /// EXPIRED BEATS ASK, and the absence of exactly this produced a waterfall. Splitting expiry into
    /// its own pass dropped the `continue` that guaranteed it: a request that had lapsed but whose
    /// archive AND its fallback both failed stayed parked with nothing registered, so the ask pass
    /// posted a fresh prompt with LIVE buttons to the owner's phone every two seconds.
    ///
    /// The unasked case is the one that matters — an expired request that nobody has asked about must
    /// still lapse rather than be asked.
    /// </summary>
    [Fact]
    public void AnExpiredRequestLapsesRatherThanBeingAsked()
    {
        Assert.Equal(
            ParkedConfirmationActions.Expire,
            ParkedConfirmation_Planner.Decide(isBeingResolved: false, isExpired: true, alreadyAsked: false));

        Assert.Equal(
            ParkedConfirmationActions.Expire,
            ParkedConfirmation_Planner.Decide(isBeingResolved: false, isExpired: true, alreadyAsked: true));
    }

    /// <summary>
    /// The kind comes from the request's own action string, so a parked file describes itself rather
    /// than relying on something remembered beside it.
    /// </summary>
    [Theory]
    [InlineData("close-orchestration", ConfirmationKinds.CloseOrchestration)]
    [InlineData("close-implementer", ConfirmationKinds.CloseImplementer)]
    [InlineData("promote-orchestration", ConfirmationKinds.PromoteOrchestration)]
    public void TheKindIsReadFromTheAction(string action, ConfirmationKinds expected)
    {
        Assert.Equal(expected, ParkedConfirmation_Planner.Resolve_Kind(action));
    }

    /// <summary>
    /// UNKNOWN IS A REFUSAL, not a default, and the caller must execute nothing on it. A parked file's
    /// actions are destructive or expensive — closing an orchestration, retiring a member, spending a
    /// crew — so an action this build does not recognise, whether from a newer build or a corrupted
    /// file, must not be guessed into the nearest match.
    ///
    /// Decision 21's shape with the opposite conclusion, deliberately: a hook that cannot evaluate its
    /// predicate ALLOWS because it only advises, and here the effect is irreversible, so the same
    /// uncertainty has to stop instead.
    /// </summary>
    [Theory]
    [InlineData("close")]
    [InlineData("promote")]
    [InlineData("close-orchestration-retry")]
    [InlineData("")]
    [InlineData(null)]
    public void AnUnrecognisedActionIsUnknownRatherThanTheClosestMatch(string? action)
    {
        Assert.Equal(ConfirmationKinds.Unknown, ParkedConfirmation_Planner.Resolve_Kind(action));
    }
}
