using AIOrchestratorCoreLib.Planning.PlanProgress;

namespace AIOrchestratorCoreLib.Planning;

/// <summary>
/// A `- [>]` LINE CLAIMS WORK IS IN FLIGHT. When nothing is running, that claim is FALSE, and this
/// is what makes the falseness visible.
///
/// WHY IT EXISTS AS CODE AND NOT AS A RULE. The rule was already written in both role commands —
/// `- [x]` means built, tested and evidenced, with only the owner's merge left, and *"the merge
/// doesn't count, it's not work, it's just a merge"*. On 2026-08-14 a session read that rule, WROTE
/// it into solo.md the same evening, and then held two finished deliverables at `- [>]` anyway
/// because their branches were not on master. The owner's answer to "sorry, my mistake" was to
/// refuse the apology and ask for the guarantee instead:
///
///   *"Ok you've sorted it out for yourself, but it absolutely must be guaranteed that it won't be
///    messed up in the future by other sessions either."*
///
/// NO TEXT MATCHING, DELIBERATELY. The tempting detector looks for "waiting on the merge" in the
/// line, and this repo has already been burned by exactly that shape: `AGENT_COACHING_SUBJECTS`
/// matched a claim's WORDING and drifted in both directions at once, which is why app entries are
/// routed on a TAG now. A phrase list would catch tonight's wording and miss the next one, while
/// reading as though it covered them.
///
/// The invariant used instead is one the app can actually observe: `[>]` means SOMEONE IS WORKING ON
/// IT. If no session of this orchestration has been mid-turn for <see cref="IDLE_MINUTES"/>, then
/// either the work is finished (`- [x]`), or it is waiting on something (`- [!]`, which says what),
/// or it was abandoned (`- [-]`). All three are one edit; none of them is `[>]`. This catches the
/// merge case without knowing anything about merges, and catches the next variant too.
/// </summary>
public static class StaleInProgress_Detector
{
    /// <summary>
    /// How long everything must be quiet before a `[>]` counts as a false claim.
    ///
    /// Generous on purpose. A session between turns, thinking, or waiting out a rate limit is not
    /// idle in any meaningful sense, and a detector that fires on those would be a nag rather than a
    /// guarantee — and a nag is what gets ignored, which is how the ledger got into this state.
    /// </summary>
    public const int IDLE_MINUTES = 10;

    /// <summary>
    /// The `[>]` lines that cannot be true, or empty when the claim is plausible.
    /// </summary>
    /// <param name="anySessionWorking">
    /// Whether ANY session of this orchestration is mid-turn, read from the usage artefacts. It is
    /// the caller's answer rather than something derived from the ledger here: `Has_WorkInFlight`
    /// consults the ledger's own `[>]` count, so using it would make this detector agree with the
    /// claim it exists to check.
    /// </param>
    /// <param name="quietFor">How long nothing has been mid-turn. Null means "not yet measured".</param>
    public static IReadOnlyList<string> Find_UnworkedInProgressLines(
        IPlanProgress? progress,
        bool anySessionWorking,
        TimeSpan? quietFor)
    {
        if (progress == null || anySessionWorking || quietFor == null)
            return [];

        if (quietFor.Value.TotalMinutes < IDLE_MINUTES)
            return [];

        return progress.InProgressTasks;
    }

    /// <summary>
    /// What the session is told, naming the three honest answers. It does NOT tell them which one to
    /// pick: the app cannot know whether the work is finished, and a guard that guesses would trade a
    /// bar that under-reports for one that lies.
    /// </summary>
    public static string Describe(IReadOnlyList<string> unworkedLines)
    {
        var lines = string.Join("\n", unworkedLines.Select(line => $"- [>] {line}"));

        return
            $"Nothing has been mid-turn for {IDLE_MINUTES} minutes, and PLAN.md still claims this is being worked on:\n\n{lines}\n\n"
            + "`- [>]` means someone is working on it right now. If it is FINISHED, mark it `- [x]` — "
            + "done means built, tested, diff read and evidence shown, and waiting for the owner to merge is NOT a "
            + "reason to hold it open (they batch their merges, so a bar that waits for master reads zero all "
            + "session). If it is waiting on something, mark it `- [!]` and say what it is waiting ON. If it was "
            + "dropped, mark it `- [-]` with the reason. If you are simply not finished, carry on — this will say "
            + "the same thing next time nothing is running.";
    }
}
