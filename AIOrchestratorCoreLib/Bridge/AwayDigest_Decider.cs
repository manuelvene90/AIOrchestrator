namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// Whether the 30-minute away digest is worth sending, or whether it would only repeat itself.
///
/// WHY THIS EXISTS — the night of 2026-08-18/19, and it is the most instructive bug this file has.
/// Away mode is the machinery that exists to STOP the owner being talked at, and it was the thing
/// doing the talking. The digest is posted onto the owner CHANNEL, and a channel append is exactly
/// what a session's watcher fires on. So every slot:
///
///   digest appended → the session's monitor wakes it → it wakes with nothing to do and writes
///   STANDING BY → that reply is suppressed (so it becomes the next "(nothing has moved)" release)
///   AND counts as traffic (so it clears the stall alert's once-per-stall token) → 30 minutes later,
///   again, forever.
///
/// `strategy-lab-4`'s log shows the limit cycle locking to an exact 30-minute period from 04:05 —
/// releases at 04:05/04:35/05:05/05:35 and alerts at 04:25/04:55/05:25 — which is
/// <see cref="PeriodicStatusSlot_Planner.SLOT_MINUTES"/> to the minute. The owner woke to about 33
/// messages and the verdict *"None of this is right"*.
///
/// The owner's call, 2026-08-19, asked as a two-option question: while away, send a digest only when
/// something CHANGED. A heartbeat that says "solo-1: inactive" twelve times over a night is not a
/// reassurance, it is the noise away mode was built to end — and because an unchanged digest is now
/// never appended, the wake that drove the loop never happens either.
///
/// COMPARED AS TEXT ON PURPOSE. The digest IS the rendering of the state the owner would read, so
/// two digests that read identically are the same message however differently they were computed —
/// and anything that genuinely changes what they would see changes these characters. A structural
/// comparison would have to be kept in step with the formatter by hand, which is the second copy
/// this repo has already been burned by (CLAUDE.md decision 12).
///
/// THE PERIODIC STATUS IS ITS SECOND CONSUMER (plan 03 Task 8, 2026-09-23, ruling R6). The re-ported
/// status is an append to the same owner channel, so it can drive the same loop through a terminal
/// session's watcher — and the complaint that got it deleted on 2026-09-09 was three IDENTICAL
/// statuses at 19:00, 19:30 and 20:00. One rule for both, rather than a second string comparison with
/// its own name: the name is the digest's because the digest came first. What the status passes in is
/// its COMPARISON form, built without the readings that move by themselves (see
/// <c>PeriodicStatusSweepModel</c>) — compared on the posted text, no two statuses would ever match.
/// </summary>
public static class AwayDigest_Decider
{
    /// <summary>
    /// True when this digest says something the last one did not.
    ///
    /// A null <paramref name="previousDigest"/> is the FIRST digest of an away spell and always
    /// sends: the owner has just been told away mode is on, and one snapshot of what they are
    /// walking away from is the thing that notice promises them. The engine drops the remembered
    /// digest when away mode ends, so the next spell starts from null again rather than silently
    /// comparing against something from hours earlier.
    /// </summary>
    public static bool Should_Send(string? previousDigest, string currentDigest)
    {
        return !string.Equals(previousDigest, currentDigest, StringComparison.Ordinal);
    }
}
