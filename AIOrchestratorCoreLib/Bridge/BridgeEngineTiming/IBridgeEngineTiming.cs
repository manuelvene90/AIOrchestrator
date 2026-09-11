namespace AIOrchestratorCoreLib.Bridge.BridgeEngineTiming;

/// <summary>
/// THE PERIODS THE BRIDGE ENGINE WAITS OUT, as something a test can shrink.
///
/// <para>
/// This is NOT a production mode and it is NOT config: <see cref="BridgeEngineTiming_Factory.Create_Production"/>
/// carries the shipped numbers and every production caller gets it, unnamed, through
/// <c>BridgeEngine_Factory.Create</c>. The reason it exists is measured rather than argued: on
/// 2026-09-08 the suite spent 107 s of wall against 27 s of CPU, and the ~350 s of that belonging to
/// the engine-driving tests was almost entirely a test asleep in front of a real 2-second tick or a
/// real 6-second aggregation window. Those tests do not assert the NUMBERS — they assert that a tick
/// eventually notices an append, that a burst arrives as one entry, that a failed channel is held
/// before it is retried. All three survive intact at a hundredth of the period, and shrinking the
/// period is the only way to buy them back without weakening what they prove.
/// </para>
/// <para>
/// WHY NOT <see cref="Time.Clock.IClock"/>. The clock answers "what time is it", which is what a
/// deadline sweep needs. None of the three below is a deadline read: two of them are how long a loop
/// SLEEPS, which no clock can shorten, and the third is handed to
/// <c>OwnerDeliveryBuffer_Factory</c> at construction. See <see cref="Time.Clock.IClock"/> for why
/// that interface is deliberately narrow and deliberately not widened by drift.
/// </para>
/// </summary>
public interface IBridgeEngineTiming
{
    /// <summary>How long the mirror loop sleeps between ticks.</summary>
    int MirrorTickMilliseconds { get; }

    /// <summary>Quiet time an owner message waits in the buffer, so a burst is delivered as ONE entry.</summary>
    int OwnerAggregationSeconds { get; }

    /// <summary>Pause before a channel whose mirror send failed is attempted again.</summary>
    int MirrorRetryBackoffSeconds { get; }

    /// <summary>
    /// How long a channel file must have stopped growing before the tailer releases its LAST entry —
    /// <c>ChannelTailer_Factory.TRAILING_ENTRY_QUIET_MILLISECONDS</c> in production, and the reasoning
    /// for the number lives there.
    /// <para>
    /// HERE BECAUSE A TEST PAYS IT IN WALL CLOCK, once per mirrored entry, exactly like the tick above:
    /// an engine-driving test that appends an entry and waits for it on the phone would sit through four
    /// real seconds for a guarantee it does not assert. It is the one period on this interface that is
    /// not a sleep — the tailer reads it as a deadline — which is why the tailer also takes an
    /// <c>IClock</c>; this knob is for the tests that drive the real loop and therefore have no clock to
    /// step.
    /// </para>
    /// </summary>
    int TrailingEntryQuietMilliseconds { get; }

    /// <summary>
    /// The whole of one mirror tick's WAITING on contended channel writes — what the tick hands to
    /// <c>ChannelWrite_Lock.Open_TickAllowance</c>. Production's value lives in
    /// <c>ChannelWrite_Lock.DEFAULT_TICK_ALLOWANCE</c> and is read from there, so there is no second
    /// copy of it; this is the seam that lets a test blocked on a lock it is HOLDING ON PURPOSE stop
    /// paying a second and a half of wall clock per tick to learn what it already knows.
    /// </summary>
    int TickLockAllowanceMilliseconds { get; }
}
