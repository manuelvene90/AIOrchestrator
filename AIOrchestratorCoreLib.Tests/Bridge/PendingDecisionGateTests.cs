using AIOrchestratorCoreLib.Bridge.Decisions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

public class PendingDecisionGateTests
{
    static readonly DateTime NowUtc = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// A payload from another button family ("hold:", "cmd:", "close-yes-", …) must fall through as
    /// NotOurs rather than be misread as one of our options — this handler cannot know what those
    /// other payloads mean, and answering on their behalf (even with a refusal) would break the
    /// documented non-collision between the five button families that share this chat.
    /// </summary>
    [Theory]
    [InlineData("hold:abc123")]
    [InlineData("cmd:restart")]
    [InlineData("close-yes-42")]
    [InlineData("garbage")]
    public void Classify_ReturnsNotOurs_ForAPayloadFromAnotherButtonFamily(string callbackData)
    {
        var outcome = PendingDecision_Gate.Classify(callbackData, found: true, expiresUtc: NowUtc.AddHours(1), isHighRisk: false, NowUtc);

        Assert.Equal(TapOutcomes.NotOurs, outcome);
    }

    /// <summary>
    /// A null payload — a tap this gate cannot even read — must also fall through as NotOurs. It
    /// must never be treated as an option of ours that happens to be malformed, because that would
    /// answer on behalf of a tap that was never meant for this handler at all.
    /// </summary>
    [Fact]
    public void Classify_ReturnsNotOurs_ForNullPayload()
    {
        var outcome = PendingDecision_Gate.Classify(null, found: true, expiresUtc: NowUtc.AddHours(1), isHighRisk: false, NowUtc);

        Assert.Equal(TapOutcomes.NotOurs, outcome);
    }

    [Fact]
    public void Classify_ReturnsUnknown_WhenThePayloadParsesButIsNotRegistered()
    {
        var outcome = PendingDecision_Gate.Classify("opt-abc123456789:0", found: false, expiresUtc: NowUtc.AddHours(1), isHighRisk: false, NowUtc);

        Assert.Equal(TapOutcomes.Unknown, outcome);
    }

    [Fact]
    public void Classify_ReturnsExpired_WhenFoundAndNowUtcIsAtTheExactExpiryInstant()
    {
        var outcome = PendingDecision_Gate.Classify("opt-abc123456789:0", found: true, expiresUtc: NowUtc, isHighRisk: false, nowUtc: NowUtc);

        Assert.Equal(TapOutcomes.Expired, outcome);
    }

    [Fact]
    public void Classify_ReturnsExpired_WhenFoundAndNowUtcIsPastExpiry()
    {
        var outcome = PendingDecision_Gate.Classify("opt-abc123456789:0", found: true, expiresUtc: NowUtc, isHighRisk: false, nowUtc: NowUtc.AddSeconds(1));

        Assert.Equal(TapOutcomes.Expired, outcome);
    }

    [Fact]
    public void Classify_ReturnsAccepted_WhenFoundLiveAndNotHighRisk()
    {
        var outcome = PendingDecision_Gate.Classify("opt-abc123456789:0", found: true, expiresUtc: NowUtc.AddMinutes(1), isHighRisk: false, NowUtc);

        Assert.Equal(TapOutcomes.Accepted, outcome);
    }

    [Fact]
    public void Classify_ReturnsNeedsConfirmation_WhenFoundLiveAndHighRisk()
    {
        var outcome = PendingDecision_Gate.Classify("opt-abc123456789:0", found: true, expiresUtc: NowUtc.AddMinutes(1), isHighRisk: true, NowUtc);

        Assert.Equal(TapOutcomes.NeedsConfirmation, outcome);
    }

    /// <summary>
    /// THE ONE THAT MATTERS. A high-risk button PAST its expiry must classify as Expired, never as
    /// NeedsConfirmation. Escalating a lapsed decision to a code prompt would let a stale keyboard —
    /// still sitting on the owner's phone from a question whose moment has passed — restart an
    /// approval flow that should have died with the deadline. Expiry is checked strictly before risk
    /// so this ordering can never be reversed by accident.
    /// </summary>
    [Fact]
    public void AHighRiskButtonPastItsExpiry_IsExpired_NeverEscalatedToNeedsConfirmation()
    {
        var outcome = PendingDecision_Gate.Classify("opt-abc123456789:0", found: true, expiresUtc: NowUtc, isHighRisk: true, nowUtc: NowUtc);

        Assert.Equal(TapOutcomes.Expired, outcome);
    }

    /// <summary>
    /// Every outcome must produce a distinct, non-empty toast — the owner sees only this string on
    /// their phone, so two different failures reading identically would recreate the exact confusion
    /// this four-way split was built to remove.
    /// </summary>
    [Theory]
    [InlineData(TapOutcomes.Accepted)]
    [InlineData(TapOutcomes.NeedsConfirmation)]
    [InlineData(TapOutcomes.Expired)]
    [InlineData(TapOutcomes.Unknown)]
    public void Describe_ForOwner_ReturnsANonEmptyStringForEveryOutcome(TapOutcomes outcome)
    {
        Assert.False(string.IsNullOrWhiteSpace(PendingDecision_Gate.Describe_ForOwner(outcome)));
    }

    /// <summary>
    /// Unknown (never registered) and Expired (registered, but past its deadline) must describe
    /// differently — telling those two failures apart in the log and to the owner is the entire
    /// point of replacing the old single "expired" catch-all.
    /// </summary>
    [Fact]
    public void UnknownAndExpired_DoNotDescribeIdentically()
    {
        Assert.NotEqual(
            PendingDecision_Gate.Describe_ForOwner(TapOutcomes.Unknown),
            PendingDecision_Gate.Describe_ForOwner(TapOutcomes.Expired));
    }
}
