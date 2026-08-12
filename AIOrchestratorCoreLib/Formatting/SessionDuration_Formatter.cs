namespace AIOrchestratorCoreLib.Formatting;

/// <summary>Human durations for owner-facing text ("2 h 14 min"), shared by every surface.</summary>
public static class SessionDuration_Formatter
{
    /// <summary>
    /// Reads an AGENT-WRITTEN stamp, or refuses it. THE one place that decides whether such a stamp
    /// can be believed, because there were two: this file returned null for a future stamp while a
    /// separate comparison happily ranked one as the most recent entry — so a single message could
    /// print no duration for an entry and simultaneously promote it as the latest. Item 12.
    ///
    /// Unparseable is refused, and so is anything beyond the skew tolerance in the FUTURE: a
    /// supervisor really did stamp 2026-08-11 01:34 on an entry written at 15:20 the day before.
    /// </summary>
    public static bool Try_ReadTrustedStamp(string stampText, DateTime now, out DateTime stamp)
    {
        if (!DateTime.TryParse(stampText, out stamp))
            return false;

        return now - stamp >= -CLOCK_SKEW_TOLERANCE;
    }

    public static string Describe(TimeSpan duration)
    {
        if (duration.Ticks < 0)
            return "now";

        if (duration.TotalMinutes < 1)
            return "under a minute";
        if (duration.TotalHours < 1)
            return $"{(int)duration.TotalMinutes} min";
        if (duration.TotalDays < 1)
            return $"{(int)duration.TotalHours} h {duration.Minutes} min";

        return $"{(int)duration.TotalDays} d {duration.Hours} h";
    }

    /// <summary>
    /// How far ahead of our clock an agent-written stamp may sit and still count as "just now".
    /// Covers a minute-rounded stamp written moments before we read it; beyond it, the writer's
    /// clock is simply wrong.
    /// </summary>
    static readonly TimeSpan CLOCK_SKEW_TOLERANCE = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Elapsed time since a timestamp an AGENT wrote into a channel header — untrusted input, so
    /// null means "say nothing" rather than guessing. A future stamp used to render as "under a
    /// minute" through a second, negative-unsafe copy of <see cref="Describe"/> in the UI, so a
    /// member that had been working for hours showed "on task under a minute". Observed 2026-08-10:
    /// a supervisor stamped 2026-08-11 01:34 on an entry written at 15:20 the day before.
    /// </summary>
    public static string? Describe_SinceStamp_OrNull(string stampText, DateTime now)
    {
        if (!Try_ReadTrustedStamp(stampText, now, out var stamp))
            return null;

        var elapsed = now - stamp;

        if (elapsed < TimeSpan.Zero)
            return Describe(TimeSpan.Zero);

        return Describe(elapsed);
    }
}
