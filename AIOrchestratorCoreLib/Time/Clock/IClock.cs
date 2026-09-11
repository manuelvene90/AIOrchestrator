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
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
