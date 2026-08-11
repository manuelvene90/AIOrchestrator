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
    /// Whether two readings describe the SAME window instance. Two unidentified readings count as
    /// the same instance: nothing has been shown to have changed, so a latch must not be dropped on
    /// the strength of an absent field.
    /// </summary>
    public static bool Is_SameInstance<T>(T? candidate, T? known) where T : struct, IComparable<T>
    {
        return Compare_Instance(candidate, known) == 0;
    }
}
