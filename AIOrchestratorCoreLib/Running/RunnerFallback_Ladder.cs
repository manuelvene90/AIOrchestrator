namespace AIOrchestratorCoreLib.Running;

/// <summary>
/// THE ONE SWITCH, AND WHAT HAPPENS WHEN THE RUNG UNDER IT BREAKS. The supervisor-mode study made
/// its recommendation conditional on a documented fallback ladder — <c>stream</c> is the fastest
/// transport but the only UNDOCUMENTED one, so the app must be able to walk down without the owner
/// rewriting anything: <c>stream</c> → <c>bg</c> → <c>print</c>, one word per role in config.json.
///
/// <para>
/// A RUNG CAN BE MISSING, AND SAYING SO IS THE POINT. <c>bg</c> is not implemented as a transport in
/// this stage (it needs the messaging socket for its wake-up, which this stage's brief excludes),
/// so <see cref="Next_Implemented_OrNull"/> steps over it and the caller logs that it did. The
/// alternative — leaving <c>bg</c> out of the ladder altogether — would make a future stage's
/// insertion silent, and would let a configuration name a transport nothing here has an opinion
/// about.
/// </para>
/// <para>
/// <see cref="SessionRunners.Terminal"/> is not on the ladder: it is not a degraded automatic
/// transport but the owner's own choice of a window they can type into, and falling INTO it
/// automatically would spawn a terminal nobody asked for on a machine that may have no display.
/// </para>
/// </summary>
public static class RunnerFallback_Ladder
{
    public static readonly IReadOnlyList<SessionRunners> ORDER = [SessionRunners.Stream, SessionRunners.Bg, SessionRunners.Print];

    /// <summary>Whether this stage can actually run the transport. False for <see cref="SessionRunners.Bg"/>.</summary>
    public static bool Is_Implemented(SessionRunners runner)
    {
        return runner != SessionRunners.Bg;
    }

    /// <summary>The next rung down, implemented or not — null at the bottom and for runners off the ladder.</summary>
    public static SessionRunners? Next_OrNull(SessionRunners runner)
    {
        var position = ORDER.ToList().IndexOf(runner);

        return position < 0 || position + 1 >= ORDER.Count ? null : ORDER[position + 1];
    }

    /// <summary>
    /// The next rung this stage can actually run, skipping the ones it cannot. Null means there is
    /// nothing below — the caller stalls the session rather than pretending it degraded.
    /// </summary>
    public static SessionRunners? Next_Implemented_OrNull(SessionRunners runner)
    {
        var next = Next_OrNull(runner);

        while (next != null && !Is_Implemented(next.Value))
            next = Next_OrNull(next.Value);

        return next;
    }

    public static string Describe_Fallback(string memberId, SessionRunners from, SessionRunners to, string reason)
    {
        var skipped = Next_OrNull(from);
        var over = skipped != null && skipped.Value != to ? $" (over '{SessionRunner_Names.Get_Word(skipped.Value)}', which this stage does not implement)" : string.Empty;

        return $"'{memberId}' falls back from runner '{SessionRunner_Names.Get_Word(from)}' to '{SessionRunner_Names.Get_Word(to)}'{over} — {reason}";
    }
}
