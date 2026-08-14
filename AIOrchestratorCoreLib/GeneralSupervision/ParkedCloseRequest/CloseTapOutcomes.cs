namespace AIOrchestratorCoreLib.GeneralSupervision.ParkedCloseRequest;

/// <summary>
/// What actually happened after the owner tapped — which is not the same question as what they tapped,
/// and the difference is the reason this type exists.
///
/// The prompt used to be edited to "✅ Closed — you confirmed" BEFORE the close was attempted, with a
/// comment explaining that an edit sent afterwards would have nowhere to land once the topic was
/// deleted. So a close that did nothing, and a close that half-happened, both reported success to the
/// owner and nothing anywhere corrected them.
/// </summary>
public enum CloseTapOutcomes
{
    /// <summary>The owner tapped "keep it open". Nothing was attempted and nothing needs to be.</summary>
    Declined,

    /// <summary>The close ran to completion.</summary>
    Closed,

    /// <summary>
    /// NOTHING WAS TOUCHED — the parked request could not be read, so there was no authority to end
    /// anything. This one heals itself: the request is deliberately left parked and the owner is asked
    /// again, so what they need to know is that the tap they just made did not take.
    /// </summary>
    NotAttempted,

    /// <summary>
    /// THE EXECUTOR THREW, AND WE DO NOT KNOW HOW FAR IT GOT. The orchestration is marked closed
    /// before its sessions are killed, so a failure between those two steps leaves it flagged as
    /// closed with its terminals still running.
    ///
    /// This is the outcome that must not be rendered as either of the others. It does not heal — the
    /// store already says closed, so no sweep will re-offer the request — and the owner would
    /// otherwise be left permanently believing that sessions still burning tokens are dead.
    /// </summary>
    Uncertain,
}
