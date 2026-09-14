namespace AIOrchestratorCoreLib.Running.TurnLiveness;

/// <summary>
/// THE BRAKE THAT WATCHES WORK, NOT THE CLOCK — for a member's print turn. It kills a turn only when
/// the turn shows NO sign of life for <see cref="SilenceLimit"/>, and every sign it can read counts:
/// output on the turn's own pipes, a write to its transcript or to any of its sub-agents', and a
/// command still running below its process.
///
/// <para>
/// WHY, measured on the VPS 2026-09-11 (spec <c>2026-09-11-the-brake-that-watches-work.md</c>): the
/// thirty-minute deadline killed 58 member turns in three days and not one was a hang — in the five
/// minutes before the kill they were still making ~900 model calls between them. A clock cannot tell
/// a turn that is stuck from one that is busy; this can, and the deadline stays behind it as the
/// backstop for a turn that keeps showing life without ever converging.
/// </para>
/// <para>
/// ANY ONE SIGN IS ENOUGH, AND "CANNOT TELL" COUNTS AS A SIGN (owner, 2026-09-11: "the best and the
/// safest"). The cost of that direction is a hung turn that holds a live child process lives on to
/// the deadline, exactly as it does today; the cost of the other direction is killing live work,
/// which is the defect this exists to remove.
/// </para>
/// </summary>
public interface ITurnSilenceBrake
{
    /// <summary>How long a turn may show no sign of life before it is killed.</summary>
    TimeSpan SilenceLimit { get; }

    /// <summary>How often the runner asks. Small against the limit, so a kill lands close to it.</summary>
    TimeSpan PollInterval { get; }

    /// <summary>
    /// Null while the turn is alive; otherwise the line that says why it is being killed, naming
    /// the limit and the config key that sets it, so whoever reads the log can change it.
    /// <paramref name="lastOutputUtc"/> is the last byte on the turn's stdout or stderr, null if none yet.
    /// </summary>
    string? Decide_Kill_OrNull(DateTime nowUtc, DateTime turnStartedUtc, DateTime? lastOutputUtc, int processId);
}
