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
/// THE FLOOR IS THE OWNER'S TO LOWER NOW (plan 03, `pulse.stepMinutes`), and lowering it has a price
/// that was measured, not guessed: on 2026-09-10 a per-minute clock made `editMessageText` fire once a
/// minute per live topic and Telegram answered 429 with `retry_after` 20-32 seconds, once a minute, per
/// topic. A one-minute step buys a finer clock with exactly that; the step is read at the engine and
/// handed in, and the constant below is only the catalogue's shipped value.
///
/// Nothing is said below <see cref="QUIET_BEFORE_SAYING_MINUTES"/>: "unchanged 2 min" is not news,
/// it is what a working orchestration looks like between two saves.
/// </summary>
public static class UnchangedFor_Formatter
{
    /// <summary>Below this the figures have not "stood still", they are simply between changes.</summary>
    public const int QUIET_BEFORE_SAYING_MINUTES = 10;

    /// <summary>
    /// The SHIPPED step — `pulse.stepMinutes`' catalogue default, and nothing else reads it. Every
    /// surface that rounds takes the resolved step as a parameter, so a value the owner set cannot be
    /// quietly outranked by this constant somewhere it was forgotten.
    /// </summary>
    public const int STEP_MINUTES = 5;

    /// <summary>
    /// Null when there is nothing worth saying — too recent, or a negative span, which is what a
    /// clock change or a future stamp produces. Item 12's rule: return nothing rather than a
    /// confident wrong number.
    ///
    /// <para>
    /// ALSO NULL BEFORE THE FIRST WHOLE STEP, which only a step coarser than
    /// <see cref="QUIET_BEFORE_SAYING_MINUTES"/> can reach: twelve minutes at a fifteen-minute step floors
    /// to zero, and zero through the duration formatter reads "under a minute" about figures that have
    /// stood still for twelve.
    /// </para>
    /// </summary>
    public static string? Describe_OrNull(TimeSpan unchangedFor, int stepMinutes)
    {
        if (stepMinutes < 1)
            throw new ArgumentOutOfRangeException(nameof(stepMinutes), stepMinutes, "An unchanged-for step is a number of minutes, at least one.");

        if (unchangedFor.TotalMinutes < QUIET_BEFORE_SAYING_MINUTES)
            return null;

        var steppedMinutes = (int)(unchangedFor.TotalMinutes / stepMinutes) * stepMinutes;

        if (steppedMinutes == 0)
            return null;

        // Through the one duration formatter this repo has, so "1 h 5 min" is spelled the same here
        // as everywhere else rather than by a second set of rules.
        return $"unchanged {SessionDuration_Formatter.Describe(TimeSpan.FromMinutes(steppedMinutes))}";
    }
}
