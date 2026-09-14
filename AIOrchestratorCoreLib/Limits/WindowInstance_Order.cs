namespace AIOrchestratorCoreLib.Limits;

/// <summary>
/// The ONE rule for ordering two readings of the same limit window by which window INSTANCE each
/// one describes.
///
/// It lives in its own component because the rule is needed in two places — the shape-pinned
/// <see cref="RateLimits_Reader"/> over reset INSTANTS, and the tolerant alert path over raw window
/// identities — and writing it twice is how this repo has been bitten before: a second copy of a
/// rule drifts from the first and only one of them gets the fix (CLAUDE.md item 12).
/// </summary>
public static class WindowInstance_Order
{
    /// <summary>
    /// Positive when the candidate describes a NEWER window than the one held, negative when older,
    /// zero when both describe the same instance or neither can be identified at all.
    ///
    /// An unidentifiable reading never displaces an identifiable one — it cannot be shown to be
    /// current — but two unidentifiable readings compare equal, which is what lets the caller fall
    /// back to its older percentage-based behaviour instead of going silent.
    ///
    /// USE THIS ONLY TO ASK "IS THIS THE SAME WINDOW", never "which reading is current": the reset
    /// stamp does not increase with time. See <see cref="Compare_Reading"/>.
    /// </summary>
    public static int Compare_Instance<T>(T? candidate, T? known) where T : struct, IComparable<T>
    {
        if (candidate == null && known == null)
            return 0;

        if (candidate == null)
            return -1;

        if (known == null)
            return 1;

        return candidate.Value.CompareTo(known.Value);
    }

    /// <summary>
    /// Which of two readings of the same window NAME describes the instance in force NOW — the
    /// question <see cref="RateLimits_Reader.Read_WorstAcrossSessions"/> and the alert scan both ask
    /// when two probe files disagree about which window exists. Positive means the candidate
    /// replaces what is held outright, zero means they describe the same instance and may compete on
    /// percentage, negative means the candidate is discarded.
    ///
    /// IT IS DECIDED BY WHEN EACH READING WAS TAKEN, NOT BY WHICH RESET STAMP IS LATER. That older
    /// rule rested on a premise the live data refutes: it assumed a later `resets_at` could only
    /// mean a newer window. Measured on this machine 2026-09-11/12 — nine probe files, one account —
    /// the WEEKLY window's reset instant moved BACKWARDS, from 2026-09-16 05:00 (reported on 09-09
    /// and 09-10) to 2026-09-14 10:00 (reported on 09-11 by every live session). The stamp-ordered
    /// rule therefore elected `fincanva-2`'s two-day-old 60% as "the newer window" and held it
    /// against four live sessions reporting 90%, where it would have stayed frozen until 09-16 —
    /// under-reporting the account's weekly usage by thirty points while it sat at 90%, and pinning
    /// the alert latch to a window nothing live was reporting, so no 90/95/… alert could fire. That
    /// is the "stuck counter" the owner reported.
    ///
    /// WHY FILE RECENCY IS SOUND HERE and is not the file-age freshness rule this repo has already
    /// rejected: `LiveLimitStillAlertsTests` forbids DISCARDING a reading for being old,
    /// because a session that hits 100% stops rewriting its probe and its file goes stale carrying
    /// the true 100%. Nothing here discards anything for age. Recency only breaks a tie between two
    /// readings that name DIFFERENT windows, and readings of the SAME window still compete on
    /// percentage exactly as before — so the stopped session's 100% still wins its own window
    /// against any number of fresher, lower readings of it.
    ///
    /// Both taken-at instants must be on the same clock (the callers use UTC file write times). The
    /// stamps are only ever compared for EQUALITY here, so they may be on a different one.
    /// </summary>
    public static int Compare_Reading<T>(
        T? candidateStamp,
        DateTime candidateTakenAtUtc,
        T? knownStamp,
        DateTime knownTakenAtUtc)
        where T : struct, IComparable<T>
    {
        // Same window — including two readings that cannot be placed at all — so recency has no say:
        // the caller's percentage rule decides, which is what keeps a stale 100% winning its window.
        if (Is_SameInstance(candidateStamp, knownStamp))
            return 0;

        // Unchanged from the stamp rule, and deliberately so: a reading with no identity cannot be
        // shown to describe the window in force, however recently it was written. The live proof is
        // `general/.usage.json` on this machine — a stamp-less 91% five_hour left behind by a
        // Windows host, which no amount of recency should let speak for the account.
        if (candidateStamp == null)
            return -1;

        if (knownStamp == null)
            return 1;

        var byRecency = candidateTakenAtUtc.CompareTo(knownTakenAtUtc);

        // Two identified windows written at the very same instant is not a real case, only a
        // reachable one. Fall back to the stamp so the answer never depends on iteration order.
        return byRecency != 0 ? byRecency : candidateStamp.Value.CompareTo(knownStamp.Value);
    }

    /// <summary>
    /// Whether two readings describe the SAME window instance. Two unidentified readings count as
    /// the same instance: nothing has been shown to have changed, so a latch must not be dropped on
    /// the strength of an absent field.
    /// </summary>
    public static bool Is_SameInstance<T>(T? candidate, T? known) where T : struct, IComparable<T>
    {
        return Compare_Instance(candidate, known) == 0;
    }
}
