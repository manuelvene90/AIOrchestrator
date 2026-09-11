namespace AIOrchestratorCoreLib.Running.PrintTurnDispatcher;

/// <summary>
/// Turns inbound channel entries into print turns, one session at a time per session. Driven by
/// the bridge's mirror tick; owns the in-flight turns and kills them on <see cref="Stop_Async"/>.
/// </summary>
public interface IPrintTurnDispatcher
{
    /// <summary>One pass over every registered print session: reads its channel, starts a turn where one is due. Never blocks on a turn.</summary>
    void Tick(DateTime nowLocal);

    /// <summary>Turns started and not yet settled — for the tests and for a future status card.</summary>
    int InFlightCount { get; }

    /// <summary>
    /// Whether THIS session has a turn running right now.
    ///
    /// <para>
    /// THE ONE TRUE ANSWER TO "IS IT WORKING", and until now nobody could ask it. The bridge inferred
    /// liveness from <c>.usage.json</c>, a file the Claude Code status line writes — which a headless
    /// <c>claude -p</c> session never renders, so on a bridge-driven host the answer was permanently
    /// "no idea", read everywhere as "idle". That fiction reached the owner as "idle — waiting" over a
    /// working member, suppressed the typing bubble, fired stall alerts against busy orchestrations,
    /// and on one path condemned members that were mid-turn.
    /// </para>
    /// <para>
    /// This dispatcher STARTED the turn, so it does not have to infer anything. Same key the in-flight
    /// map already uses.
    /// </para>
    /// </summary>
    bool Is_TurnInFlight(string orchId, string memberId);

    /// <summary>
    /// Whether THIS session's admitted turn is still WAITING FOR A SLOT — in the in-flight table,
    /// nothing executing. <see cref="Is_TurnInFlight"/> is true for both a queued and a running turn,
    /// and reading it as "working" is how the owner was narrated "still at it" for 29 minutes on
    /// 2026-09-10 about a supervisor that had not started. False when the turn is running, and false
    /// when there is no turn at all.
    /// </summary>
    bool Is_TurnQueued(string orchId, string memberId);

    /// <summary>
    /// THE /resume OVERRIDE. Drops <see cref="PrintSessionState.IPrintSessionState.RetryNotBeforeUtc"/>
    /// on every registered session that has one, so the very next tick tries the turn again instead of
    /// waiting out the appointment.
    ///
    /// <para>
    /// Found reviewing this branch, 2026-09-09: <c>/resume</c>'s own help text is "Wake EVERY session —
    /// use when the usage limit resets", but it wakes a session by appending fresh channel traffic, and
    /// <c>Consider_Session</c> refuses to start a turn before <c>RetryNotBeforeUtc</c> regardless of what
    /// is pending — so a deferred session stayed asleep through it. It also matters when the appointment
    /// itself is wrong: <see cref="Running.LimitReset_Parser"/> falls back to UTC when the zone the CLI
    /// named is unknown on this machine, which can put the retry up to a couple of hours LATER than the
    /// real reset, and until now nothing could say "go anyway".
    /// </para>
    /// <para>
    /// A session with no deferral is untouched — no rewrite, no log line. Reporting a clear that did not
    /// happen would be the same lie <c>/resume</c> exists to end, in the other direction.
    /// </para>
    /// <para>
    /// RETURNS HOW MANY IT CLEARED, so the owner's reply can say it (F7, 2026-09-09). It used to log per
    /// session and count nothing, and <c>/resume</c>'s reply counted only sessions woken — so the one
    /// thing this method exists to do was the one thing the owner was never told about.
    /// </para>
    /// <para>
    /// A SESSION WITH A TURN IN FLIGHT IS SKIPPED, not waited for (F6, reproduced 2026-09-09). This reads
    /// a state file and writes back a value derived from that snapshot; a turn running at the same time
    /// writes its own record into the same file, and the derived write landed on top of it —
    /// <c>executed_turns 1 → 0</c>, <c>next_turn 3 → 2</c>, so the same request id ran twice. Skipping
    /// costs nothing: a session that is RUNNING is not one <c>/resume</c> needs to wake, and the
    /// dispatcher now also drops the appointment the moment a deferred turn starts, so the window this
    /// closes is only the turn's own duration.
    /// </para>
    /// <para>
    /// IT NEVER THROWS PAST ITS OWN LOOP. <c>/resume</c> calls it BEFORE it appends anything, so an
    /// escaping IO failure aborted the whole command, was logged as a Telegram backoff, and had the
    /// update redelivered and retried for ever.
    /// </para>
    /// </summary>
    /// <returns>The number of sessions whose appointment was dropped.</returns>
    int Clear_LimitDeferrals();

    /// <summary>Cancels every in-flight turn (process trees killed) and waits for them to settle.</summary>
    Task Stop_Async();
}
