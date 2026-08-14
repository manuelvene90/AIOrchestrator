using AIOrchestratorCoreLib.GeneralSupervision.ParkedCloseRequest;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.GeneralSupervision;

/// <summary>
/// WHICH outcome a confirmed tap produced. The sentence each outcome maps to was already covered; this
/// selection was not, and it is the fix rather than its presentation.
///
/// The gap was exact: with the mapping pinned and the selection unreachable, changing the engine's
/// catch to return <c>Closed</c> restored the original defect — a half-close announced to the owner as
/// "✅ Closed — you confirmed" — with every sentence case still green, because each one went on mapping
/// its own outcome faithfully.
/// </summary>
public class CloseTapOutcomeDeciderTests
{
    static readonly IParkedCloseRequest REQUEST =
        ParkedCloseRequest_Factory.Create_ForOrchestration("crm-2", "supervisor of crm-2", "work is done", "parked.json");

    [Fact]
    public void ACloseThatRanToCompletionIsClosed()
    {
        Assert.Equal(CloseTapOutcomes.Closed, CloseTapOutcome_Decider.Decide(REQUEST, null));
    }

    /// <summary>
    /// THE ONE THE CHANGE EXISTS FOR. Execute_Close marks the orchestration closed before it kills the
    /// sessions, so a throw between those two leaves it flagged closed with its terminals alive — and
    /// nothing re-offers it, because the store already says closed. Reporting that as success is the
    /// original defect, and it is one edit away at all times.
    /// </summary>
    [Fact]
    public void AnExecutorThatThrewIsUncertain_NeverClosed()
    {
        var outcome = CloseTapOutcome_Decider.Decide(REQUEST, new Exception("kill failed"));

        Assert.Equal(CloseTapOutcomes.Uncertain, outcome);
        Assert.NotEqual(CloseTapOutcomes.Closed, outcome);
    }

    /// <summary>
    /// No authority, no close. An unreadable request means nothing was attempted — and it must not
    /// depend on whether anything later threw, because there is no "later" on that path.
    /// </summary>
    [Fact]
    public void AnUnreadableRequestIsNotAttempted_WhateverElseHappened()
    {
        Assert.Equal(CloseTapOutcomes.NotAttempted, CloseTapOutcome_Decider.Decide(null, null));
        Assert.Equal(CloseTapOutcomes.NotAttempted, CloseTapOutcome_Decider.Decide(null, new Exception("irrelevant")));
    }

    /// <summary>
    /// A confirmed tap never yields Declined — that outcome belongs to the other branch entirely, and
    /// the two must not be reachable from one another.
    /// </summary>
    [Fact]
    public void AConfirmedTapNeverReportsADecline()
    {
        Assert.NotEqual(CloseTapOutcomes.Declined, CloseTapOutcome_Decider.Decide(REQUEST, null));
        Assert.NotEqual(CloseTapOutcomes.Declined, CloseTapOutcome_Decider.Decide(REQUEST, new Exception("boom")));
        Assert.NotEqual(CloseTapOutcomes.Declined, CloseTapOutcome_Decider.Decide(null, null));
    }
}
