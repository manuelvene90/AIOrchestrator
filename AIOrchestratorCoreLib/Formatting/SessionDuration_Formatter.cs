namespace AIOrchestratorCoreLib.Formatting;

/// <summary>Human durations for owner-facing text ("2 h 14 min"), shared by every surface.</summary>
public static class SessionDuration_Formatter
{
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
        if (!DateTime.TryParse(stampText, out var stamp))
            return null;

        var elapsed = now - stamp;

        if (elapsed < -CLOCK_SKEW_TOLERANCE)
            return null;

        if (elapsed < TimeSpan.Zero)
            return Describe(TimeSpan.Zero);

        return Describe(elapsed);
    }
}
