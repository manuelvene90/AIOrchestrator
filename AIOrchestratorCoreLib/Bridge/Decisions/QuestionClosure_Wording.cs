namespace AIOrchestratorCoreLib.Bridge.Decisions;

/// <summary>
/// WHAT CLOSED A QUESTION, and the line that says so when a high-risk read-back lapses on one.
///
/// <para>
/// THE LOG USED TO ASSERT SOMETHING IT HAD NOT LOOKED UP. A lapsed read-back window logged
/// "nothing was taken, and the question is still open" unconditionally — the sweep never read the
/// open-question registry. On 2026-09-09 at ~16:03Z that line was written about a question that had
/// already been stamped closed minutes earlier, so the one record of the event told a reader the
/// opposite of the truth and pointed them at a question they would not find.
/// </para>
/// <para>
/// SO THE REASON IS REMEMBERED WHERE THE CLOSURE HAPPENS, and it is a fixed vocabulary rather than
/// free text: a reader comparing two log lines a week apart must not have to decide whether "closed
/// by the owner" and "answered by message" are the same event.
/// </para>
/// <para>
/// AN UNRECORDED CLOSURE IS SAID OUT LOUD TOO. The reasons live in memory only — they are a
/// diagnostic, not state anything acts on — so a closure that happened before a restart is named as
/// exactly that, never guessed at and never silently reported as "still open" again.
/// </para>
/// </summary>
public static class QuestionClosure_Wording
{
    public const string TAPPED_OPTION = "an option the owner tapped";
    public const string TALK_REQUEST = "the owner asking to talk it through";
    public const string TYPED_ANSWER = "an answer the owner typed";
    public const string CONFIRMED_HIGH_RISK = "a high-risk choice confirmed by code";
    public const string DEADLINE = "its own deadline";
    public const string AWAY_PARKED = "away mode parking it";
    public const string SUPERSEDED = "a newer question, asked after the owner had replied in words";
    public const string UNRECORDED = "something this process no longer remembers — it restarted since";

    const string PREAMBLE = "A high-risk read-back window closed with no code typed — nothing was taken, and ";

    /// <summary>
    /// <paramref name="closureReason"/> is null when the question is gone and no reason was
    /// recorded for it; it is ignored entirely while the question is still open.
    /// </summary>
    public static string Describe_LapsedReadBack(bool questionStillOpen, string? closureReason)
    {
        if (questionStillOpen)
            return $"{PREAMBLE}the question is still open";

        return $"{PREAMBLE}the question was already closed — by: {(string.IsNullOrWhiteSpace(closureReason) ? UNRECORDED : closureReason)}";
    }
}
