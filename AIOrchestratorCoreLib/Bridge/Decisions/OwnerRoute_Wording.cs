namespace AIOrchestratorCoreLib.Bridge.Decisions;

/// <summary>What became of an owner message the bridge tried to route.</summary>
public enum OwnerRouteOutcomes
{
    /// <summary>It reached the channel the owner was writing into. The only outcome a ✓ may follow.</summary>
    Routed,

    /// <summary>No orchestration owns that topic — a leftover topic, or one from another install.</summary>
    UnknownTopic,

    /// <summary>The orchestration is closed. Its channel file is an archive, not a conversation.</summary>
    ClosedOrchestration,

    /// <summary>
    /// Nothing was appended and the owner has ALREADY been answered directly — a voice note with no
    /// transcriber configured, a transcription that failed, a document over the size cap. A ✓ after
    /// one of those says "received" underneath a message that just said "I could not take it".
    /// </summary>
    AnsweredDirectly,
}

/// <summary>
/// THE RECEIPT MUST MEAN WHAT IT SAYS. `Send_ReceivedAck_Async` ran after the routing call whatever
/// the routing did, so an owner message into an unknown topic was dropped with a warning and
/// acknowledged with a ✓ on the same screen; a message into a CLOSED orchestration was written into
/// a channel nobody tails and acknowledged the same way. The owner's own words for what this brief
/// is about: the bridge must never "tell me it was received when it was not".
///
/// <para>
/// HELD IS NOT ONE OF THESE OUTCOMES, deliberately, and the brief's enum listed it. A held message
/// IS routed — it lands in the delivery buffer and reaches the session on GO — so the hold is a
/// decision the caller makes AFTER a successful route about which receipt to show (the counting ⏸
/// receipt instead of the ✓). Making it an outcome here would put one fact in two places.
/// </para>
/// <para>
/// The strings are ENGLISH like every other string the app writes (owner's rule, 2026-09-09).
/// </para>
/// </summary>
public static class OwnerRoute_Wording
{
    /// <summary>Whether a "received" tick may follow this outcome.</summary>
    public static bool Deserves_Receipt(OwnerRouteOutcomes outcome)
    {
        return outcome == OwnerRouteOutcomes.Routed;
    }

    /// <summary>
    /// The one line the owner is told instead of a ✓, or null when they need no line — either
    /// because the message arrived (Routed) or because they have already been answered
    /// (AnsweredDirectly, whose own path said what went wrong and what to do).
    ///
    /// <para>
    /// EVERY REFUSAL NAMES THE WAY OUT. A message that lands nowhere is the failure the owner
    /// reported; a message that lands nowhere and says so without telling them what to do next is
    /// the same failure with better manners.
    /// </para>
    /// </summary>
    public static string? Describe_ForOwner_OrNull(OwnerRouteOutcomes outcome)
    {
        return outcome switch
        {
            OwnerRouteOutcomes.ClosedOrchestration =>
                "🏁 this orchestration is closed — nothing here is being read any more. Ask the general "
                + "supervisor in General to reopen the work or start a new one.",
            OwnerRouteOutcomes.UnknownTopic =>
                "❓ this topic belongs to no orchestration I know — your message was not delivered. "
                + "Write in General and the general supervisor will place it.",
            _ => null,
        };
    }
}
