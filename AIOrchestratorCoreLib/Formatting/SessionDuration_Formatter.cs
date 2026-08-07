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
}
