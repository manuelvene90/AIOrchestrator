namespace AIOrchestratorCoreLib.Formatting;

/// <summary>
/// How long the ledger figures on the auto-updating status line have stood still.
///
/// The owner asked for this INSTEAD of a delta on that surface, and their reasoning is the whole
/// design: *"The difference doesn't make sense because at best, after updating every 10 seconds it
/// would go back to 0."* A delta belongs on the half-hourly message, which has a real baseline; this
/// line has none, so what it can say is how long nothing has moved.
///
/// COARSE ON PURPOSE, and this is the part a future edit will want to undo. The status line is
/// EDITED whenever its text changes, and <see cref="Telegram.TopicStatusLine_Decider"/> exists to
/// answer "nothing has changed" cheaply — a duration ticking by the minute would make the text
/// differ on every single tick, turning the quietest topic into the busiest one and defeating the
/// one optimisation that surface has. Five-minute steps bound it to about twelve edits an hour.
///
/// Nothing is said below <see cref="QUIET_BEFORE_SAYING_MINUTES"/>: "unchanged 2 min" is not news,
/// it is what a working orchestration looks like between two saves.
/// </summary>
public static class UnchangedFor_Formatter
{
    /// <summary>Below this the figures have not "stood still", they are simply between changes.</summary>
    public const int QUIET_BEFORE_SAYING_MINUTES = 10;

    /// <summary>The step the reported figure is rounded DOWN to, so the text changes rarely.</summary>
    public const int STEP_MINUTES = 5;

    /// <summary>
    /// Null when there is nothing worth saying — too recent, or a negative span, which is what a
    /// clock change or a future stamp produces. Item 12's rule: return nothing rather than a
    /// confident wrong number.
    /// </summary>
    public static string? Describe_OrNull(TimeSpan unchangedFor)
    {
        if (unchangedFor.TotalMinutes < QUIET_BEFORE_SAYING_MINUTES)
            return null;

        var steppedMinutes = (int)(unchangedFor.TotalMinutes / STEP_MINUTES) * STEP_MINUTES;

        // Through the one duration formatter this repo has, so "1 h 5 min" is spelled the same here
        // as everywhere else rather than by a second set of rules.
        return $"unchanged {SessionDuration_Formatter.Describe(TimeSpan.FromMinutes(steppedMinutes))}";
    }
}
