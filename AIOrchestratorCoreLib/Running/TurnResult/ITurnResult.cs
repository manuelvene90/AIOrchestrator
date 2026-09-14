namespace AIOrchestratorCoreLib.Running.TurnResult;

/// <summary>
/// What one <c>claude -p --output-format json</c> invocation came back with. Every JSON-derived
/// field is nullable: the parser is tolerant like <c>Limits.LimitData_Parser</c>, because a field
/// missing from a newer CLI is a fact to record, not a reason to lose the turn.
/// </summary>
public interface ITurnResult
{
    int ExitCode { get; }
    bool TimedOut { get; }

    /// <summary>
    /// THERE IS NO HALF-DONE WORK BEHIND THIS KILL, when <see cref="TimedOut"/> is set — and the
    /// reason it is a field rather than something inferred from the elapsed time. Every kill comes
    /// back the same way (timed out, exit -1, no result document), yet they mean opposite things,
    /// and only a kill that interrupted WORK earns a closing turn
    /// (<see cref="ClosingTurn.ClosingTurn_Rule"/>). So it is MARKED where it is known — by the
    /// stream executor, which is the only place that still knows — and never guessed at later.
    ///
    /// <para>
    /// TWO PRODUCERS, both in the stream executor, both meaning "the model was never asked the
    /// question this turn is about". The first is the SILENCE kill: the heartbeat caught a process
    /// that had said nothing for the silence limit, so it was not working. The second is the BOOT:
    /// a session whose role command alone outlived the turn timeout never received its brief at all
    /// (adversarial review, 2026-09-09 — it returned as an ordinary deadline kill, so it was given a
    /// closing turn that resumed a transcript containing nothing but the boot, and the brief the
    /// session had never seen was marked delivered).
    /// </para>
    /// </summary>
    bool NothingToClose { get; }

    /// <summary>
    /// THE LINE THE MEMBER SILENCE BRAKE WROTE when it killed this turn, null for every other ending —
    /// a deadline kill included. <see cref="TimedOut"/> is set either way, because both are kills of a
    /// turn that had been WORKING and both earn the closing turn; this is what lets the log and the
    /// channel say which of the two happened instead of calling a silence a deadline
    /// (<see cref="TurnLiveness.ITurnSilenceBrake"/>).
    /// </summary>
    string? BrakeKill { get; }

    /// <summary>The CLI's own verdict (<c>is_error</c>); false when the JSON was absent.</summary>
    bool IsError { get; }
    string? Subtype { get; }

    /// <summary>The session's final message — what becomes its channel entry.</summary>
    string? ResultText { get; }

    /// <summary>
    /// THE FINAL MESSAGES THIS TURN WROTE BEFORE THE ONE THAT BECAME <see cref="ResultText"/> —
    /// empty on every ordinary turn, and the whole point of this member when it is not.
    ///
    /// <para>
    /// A session's final message IS its channel entry, and only the <c>result</c> event's text was
    /// ever filed. Measured three times on 2026-09-09/10: a session wrote its report, a BACKGROUND
    /// sub-agent (<c>Task</c> with <c>run_in_background</c>) returned afterwards, the CLI re-opened
    /// the turn, and the later message became the result — so a 19,771-character report and, on
    /// another run, a nine-agent review were both LOST, with the turn reporting success. Nothing
    /// upstream can tell those two messages apart after the fact; only the transport, watching the
    /// events go by, still knows there was an earlier one.
    /// </para>
    /// <para>
    /// Carried here in ORDER, oldest first, so the dispatcher can file each one as its own entry
    /// before the turn's own. <see cref="SupersededFinals_Rule"/> owns what counts as one.
    /// </para>
    /// </summary>
    IReadOnlyList<string> SupersededFinals { get; }
    string? SessionId { get; }
    double? TotalCostUsd { get; }
    long? DurationMs { get; }
    long? DurationApiMs { get; }
    int? NumTurns { get; }
    int? ApiErrorStatus { get; }
    string RawStdout { get; }
    string RawStderr { get; }
    TimeSpan Elapsed { get; }
}
