using AIOrchestratorCoreLib.Running.ExecutedTurn;
using AIOrchestratorCoreLib.Running.TurnResult;

namespace AIOrchestratorCoreLib.Running.ClosingTurn;

/// <summary>
/// WHICH KILL EARNS A CLOSING TURN — one question, answered in one place, because the two kills
/// look identical from the dispatcher's side and mean opposite things.
///
/// <para>
/// A DEADLINE KILL is a turn that ran out of its 30 minutes: measured on the VPS, those turns were
/// working (32–92 tool calls) and had something to report. A SILENCE KILL is the heartbeat catching
/// a process that has said nothing for the silence limit — it produced no bytes at all, so there is
/// nothing to close down and today's retry is the right answer. Both come back with
/// <see cref="ITurnResult.TimedOut"/> set and exit -1, which is why the silence kill has to be
/// MARKED at the place that knows (<see cref="TurnResult_Factory.CreateFrom_SilenceKill"/>, called
/// by the stream executor) rather than guessed at from a duration here.
/// </para>
/// </summary>
public static class ClosingTurn_Rule
{
    public static bool Is_DeadlineKill(ITurnResult result)
    {
        return result.TimedOut && !result.NothingToClose;
    }

    /// <summary>
    /// How long the closing turn gets: <see cref="ClosingTurn_Words.TIMEOUT"/>, or the session's own
    /// turn timeout when that is shorter. A closing turn is never given more time than the turn it
    /// is closing — and the clamp is also what lets the failure branch be tested in seconds rather
    /// than in five minutes.
    /// </summary>
    public static TimeSpan Resolve_Timeout(TimeSpan turnTimeout)
    {
        return turnTimeout < ClosingTurn_Words.TIMEOUT ? turnTimeout : ClosingTurn_Words.TIMEOUT;
    }

    /// <summary>
    /// HOW LONG A STOP MUST WAIT FOR ONE SESSION'S WORST CASE: the work turn's own timeout, plus the
    /// closing turn a deadline kill may still be owed, plus <paramref name="margin"/> for the
    /// bookkeeping either side of them.
    ///
    /// <para>
    /// IT IS A SUM BECAUSE THE TWO RUN BACK TO BACK. The drain grace was written when a turn could
    /// only be its own timeout long, and this stage added minutes after the kill without resizing it
    /// (adversarial review, 2026-09-09): 30 + 5 of need against 30 + 1 of grace in production, and
    /// probed at a 72-second turn timeout the closing turn was cancelled mid-flight — its money and
    /// its minutes spent, nothing recorded, the entries re-run at the next start anyway. A grace
    /// shorter than the path it is draining does not shorten the shutdown, it just throws away the
    /// last thing the session paid for.
    /// </para>
    /// </summary>
    public static TimeSpan Resolve_DrainGrace(TimeSpan turnTimeout, TimeSpan margin)
    {
        return turnTimeout + Resolve_Timeout(turnTimeout) + margin;
    }

    /// <summary>
    /// HOW MANY TURNS IN A ROW THIS SESSION HAS DIED AT THE DEADLINE, read off the executed list
    /// rather than counted into a new field — <see cref="TurnOutcomes.TIMEOUT"/> reaches
    /// <see cref="IExecutedTurn.Outcome"/> from exactly ONE place, the closing-turn path, because
    /// every other route to an executed record is gated on <see cref="TurnOutcomes.Is_Success"/>.
    /// One route in, so the count cannot be right for the wrong reason, and it survives a restart
    /// because the state file does.
    /// </summary>
    public static int Count_TrailingDeadlineKills(IReadOnlyList<IExecutedTurn> executedTurns)
    {
        var kills = 0;

        for (var index = executedTurns.Count - 1; index >= 0 && executedTurns[index].Outcome == TurnOutcomes.TIMEOUT; index--)
            kills++;

        return kills;
    }
}
