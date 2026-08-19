namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// What `/merge` hands the session — the whole landing ritual, in one instruction.
///
/// The owner asks for this "very often" (2026-08-19) and had been spelling it out each time, which
/// is how a step gets dropped: the wording varied, and so did what actually happened.
///
/// IT DRIVES THE SESSION, NOT THE APP, and that was their call once the trade was named. The app has
/// no build step and no test runner, so an app-side merge would be this ritual with its one real
/// safety check removed — a branch and a master that are each green can be red once combined, and
/// the suite on the MERGED tree is what catches that. Faster and quietly worse is not a trade worth
/// making silently.
///
/// The text lives here rather than at the call site because it is a CONTRACT: every session that
/// receives it must do the same things in the same order, and a string built inline is a string that
/// drifts per caller.
/// </summary>
public static class MergeRitual_Wording
{
    public const string SUBJECT = "/merge — land the work and clean up";

    /// <summary>
    /// Ordered, and the order is the point: nothing is pushed before the merged tree is green, and
    /// nothing is deleted before it is on the remote.
    /// </summary>
    public static string Build()
    {
        return
            "The owner ran /merge. Do the whole ritual, in this order, and stop at the first step that fails:\n\n"
            + "1. Commit or report anything uncommitted that is YOURS. Never `git add -A` — stage by explicit path.\n"
            + "2. Merge your working branch into the default branch with `--no-ff`, matching this repo's history.\n"
            + "3. BUILD, then run the FULL suite ON THE MERGED TREE. This is the step the app cannot do for "
            + "you and the reason you were asked rather than the app: a branch and a master that are each green "
            + "can be red once combined.\n"
            + "4. If anything is red: STOP. Do not push, do not delete anything, and report what failed with the "
            + "actual output. A failed merge left unpushed costs nothing; a broken master costs everyone.\n"
            + "5. Push only once it is green.\n"
            + "6. Then clean: delete local branches CONTAINED IN the default branch with `git branch -d` (never "
            + "`-D`, which would discard unmerged work), prune worktrees, and delete remote branches that are "
            + "contained in the default branch.\n"
            + "7. Report: the merge commit, the test numbers you actually read, and what was deleted.\n\n"
            + "SAY WHAT WAS TRUE, NOT WHAT SOUNDS COMPLETE. If there were no remote branches to delete, say "
            + "there were none — \"remote branches cleaned\" when nothing was deleted is a true-sounding empty "
            + "claim, and the owner cannot tell the difference from their phone.";
    }
}
