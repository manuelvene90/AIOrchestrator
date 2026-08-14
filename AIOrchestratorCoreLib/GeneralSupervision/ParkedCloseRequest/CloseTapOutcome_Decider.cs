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

    /// <summary>
    /// What the RESOLVED archive records — the one artefact that outlives the prompt, and the thing a
    /// person reconstructing an incident actually reads.
    ///
    /// It filed "closed" whether or not the executor threw, so a half-close was archived
    /// indistinguishably from a clean one: the record asserted precisely what the owner's sentence was
    /// changed to stop asserting. The vocabulary already separates `unreadable`, `moot`, `declined` and
    /// `expired`, so it distinguishes everywhere except the one case that cannot be told apart later.
    /// </summary>
    public static string Describe_ForArchive(CloseTapOutcomes outcome)
    {
        return outcome switch
        {
            CloseTapOutcomes.Closed => "closed",
            CloseTapOutcomes.Uncertain => "uncertain",
            CloseTapOutcomes.Declined => "declined",

            // NOT AN OVERSIGHT — this path archives NOTHING. An unreadable request is deliberately
            // left parked so the owner can be asked again, and archiving it would throw away a close
            // they had already approved. The invariant is worth stating as a throw rather than leaving
            // as an absence somebody later fills in with a plausible-looking string.
            CloseTapOutcomes.NotAttempted => throw new ArgumentOutOfRangeException(
                nameof(outcome),
                "a close that was never attempted is left parked, not archived"),

            _ => throw new ArgumentOutOfRangeException(nameof(outcome), $"unhandled close outcome '{outcome}'"),
        };
    }
}
