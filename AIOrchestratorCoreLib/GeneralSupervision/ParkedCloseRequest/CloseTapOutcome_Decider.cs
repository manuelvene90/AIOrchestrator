namespace AIOrchestratorCoreLib.GeneralSupervision.ParkedCloseRequest;

/// <summary>
/// WHICH outcome a confirmed tap produced — the selection itself, not the sentence it maps to.
///
/// The mapping from outcome to wording was already out here and covered; this was not, and the
/// difference is the whole fix. Changing the engine's catch to return <c>Closed</c> instead of
/// <c>Uncertain</c> restores the original defect exactly — a half-close reported to the owner as
/// "✅ Closed — you confirmed" — and every sentence case stays green, because each one still maps its
/// own outcome faithfully. A fix to an owner-facing lie that can be silently reverted leaves the owner
/// one edit away from being told it again.
///
/// IT TAKES THE THINGS THEMSELVES, NOT DERIVED FLAGS. Handing this a <c>bool requestWasReadable</c>
/// would move the decision to the call site and out of reach again: a mutation could pass <c>true</c>
/// where it means <c>false</c> and nothing here would redden. The same lesson is written into
/// <see cref="Telegram.TopicStatusLine_Planner"/>, which takes the message id rather than a flag
/// derived from it, for exactly this reason.
/// </summary>
public static class CloseTapOutcome_Decider
{
    /// <param name="request">The parked request, or null when it could not be read.</param>
    /// <param name="failure">What the executor threw, or null when it ran to completion.</param>
    public static CloseTapOutcomes Decide(IParkedCloseRequest? request, Exception? failure)
    {
        // NO AUTHORITY, NO CLOSE. An unreadable request means nothing was attempted, and it must not
        // depend on whether something later threw — there is no "later" on that path.
        if (request == null)
            return CloseTapOutcomes.NotAttempted;

        return failure == null ? CloseTapOutcomes.Closed : CloseTapOutcomes.Uncertain;
    }
}
