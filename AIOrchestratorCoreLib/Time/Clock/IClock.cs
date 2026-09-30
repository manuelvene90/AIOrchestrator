namespace AIOrchestratorCoreLib.Time.Clock;

/// <summary>
/// The current instant, as something a test can move.
///
/// <para>
/// DELIBERATELY NARROW, AND DELIBERATELY NOT ADOPTED EVERYWHERE. This engine reads
/// <c>DateTime.UtcNow</c> at well over a hundred sites and converting them would be a rewrite of the
/// file, not a change to it. Every pure decider in this repo already takes <c>nowUtc</c> as a
/// parameter — <see cref="Telegram.TelegramAttempt_Gate.Is_AttemptDue"/> is the pattern — and that
/// remains the idiom for anything testable in isolation.
/// </para>
/// <para>
/// What that idiom cannot pin is a deadline the ENGINE has to notice on its own: "two hours pass and
/// the default is applied" is a property of the tick, not of a function, and the only honest
/// alternative is a test that sleeps for two hours. So the clock is injected for the deadline sweep
/// and the dispatcher pause, and for nothing else. Widening it later is a change to make on purpose,
/// not by drift.
/// </para>
/// <para>
/// WIDENED ON PURPOSE ONCE, for the same reason (plan 03 task 18, 2026-09-23): the away check and the
/// owner-silence stamp it reads. Away mode is a deadline the engine notices on its own, and once its
/// delay became a setting (<c>away.afterMinutes</c>, 60 under classic) the claim "twenty minutes is not
/// away, an hour is" could only be asserted by moving a clock. Both halves moved together — a stamp on
/// one clock compared against a reading of another is how the status-line back-off once went inert.
/// </para>
/// <para>
/// AND A SECOND TIME (owner, 2026-09-30): the status line's move waits until the SESSION's last message
/// in the topic is a minute old — a deadline the engine notices on its own. The stamp (taken when the
/// mirror sends a session's entry) and the reading it is compared with (the status-line refresh) both
/// come from here, carried together in <c>TopicStatusLine_Planner.TopicSessionSilence</c>. The planner's
/// other clock — the LOCAL one its durations read agent-written stamps against — is untouched.
/// </para>
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
