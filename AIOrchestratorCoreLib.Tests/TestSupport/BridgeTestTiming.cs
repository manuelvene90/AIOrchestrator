using AIOrchestratorCoreLib.Bridge.BridgeEngineTiming;

namespace AIOrchestratorCoreLib.Tests.TestSupport;

/// <summary>
/// THE PERIODS EVERY ENGINE-DRIVING TEST RUNS THE BRIDGE ON, and the one place that says how long a
/// wall-clock window has to be to contain a given number of ticks.
///
/// <para>
/// MEASURED, NOT PREFERRED. On 2026-09-08 this suite took 98 s of wall for 29 s of CPU: the engine
/// tests were asleep in front of a real 2-second tick and a real 6-second aggregation window, and
/// the thirty slowest tests alone summed to 329 s. Not one of them asserts those numbers — they
/// assert that a tick eventually notices an append, that a burst arrives as one entry, that a failed
/// channel is held before being retried. Every one of those survives at a hundredth of the period.
/// </para>
/// <para>
/// WHY THE WINDOWS ARE COMPUTED AND NOT TYPED. The old files were full of literals — <c>4_000</c>
/// to let a couple of ticks pass, <c>12_000</c> to prove something never arrived — and each one was
/// really "N ticks" written out at the period of the day. Written as a literal it silently stops
/// meaning N ticks the moment the period changes, in whichever direction hurts: too short and the
/// test is flaky, too long and it is the sleep this file exists to remove. <see cref="Window_ForTicks"/>
/// keeps the intent and lets the period move.
/// </para>
/// </summary>
internal static class BridgeTestTiming
{
    /// <summary>
    /// Short enough to be free, long enough that the loop is not hot: at 20 ms a test that needs
    /// three ticks waits 60 ms where it used to wait 6 s.
    /// </summary>
    public const int TICK_MILLISECONDS = 20;

    /// <summary>
    /// ONE SECOND IS THE FLOOR, not a choice — <c>OwnerDeliveryBuffer_Factory.Create</c> refuses
    /// anything below it, and that guard is production's, not this file's to relax.
    /// </summary>
    public const int AGGREGATION_SECONDS = 1;

    /// <summary>
    /// PRODUCTION'S OWN 30 s. Only the retry-backoff test has any business shortening this, and it
    /// shortens it in its own file where the reasoning sits next to the assertions it changes;
    /// everyone else runs the shipped number so a test cannot accidentally depend on a hold that
    /// production does not have.
    /// </summary>
    public const int RETRY_BACKOFF_SECONDS = 30;

    /// <summary>
    /// A TENTH OF PRODUCTION'S 1500 ms, and it buys the serial <c>channel-lock</c> collection back.
    /// Those tests hold a channel's lock THEMSELVES for the whole assertion window, so every tick
    /// spends the entire allowance discovering a refusal the test arranged — 1.5 s of wall clock per
    /// tick, on the one collection that cannot run in parallel with anything, which made it the
    /// suite's floor. What they assert is that the write was REFUSED and queued, and that a later
    /// tick writes it once the lock is gone; how long the refusal took to arrive is no part of it.
    ///
    /// <para>
    /// Still generous against the thing it must not break: a tick has to keep telling a contended
    /// channel apart from a free one, and an uncontended write charges ~0 ms, so 150 ms is two
    /// orders of magnitude of headroom over a local filesystem's mkdir.
    /// </para>
    /// </summary>
    public const int TICK_LOCK_ALLOWANCE_MILLISECONDS = 150;

    /// <summary>
    /// TWO TICKS, which is EXACTLY what the tailer's old quiet-poll count came to at this tick — the
    /// growth poll starts the quiet stretch and the second poll after it releases the trailing entry,
    /// the same poll that used to be the second quiet one. Production waits four seconds
    /// (<c>ChannelTailer_Factory.TRAILING_ENTRY_QUIET_MILLISECONDS</c>) and no engine test asserts that
    /// number; what they assert is that an appended entry eventually reaches the phone, which survives
    /// intact at a hundredth of the period like every other number in this file.
    /// </summary>
    public const int TRAILING_ENTRY_QUIET_MILLISECONDS = 2 * TICK_MILLISECONDS;

    /// <summary>
    /// The engine's own start-up before its first tick body completes — one config load, one
    /// session-store load, the tailer registering the channel files it has never seen. Generous on
    /// purpose: this is the only part of a window that is not tick-proportional, and the suite runs
    /// its collections in parallel on a loaded machine.
    /// </summary>
    const int STARTUP_MILLISECONDS = 600;

    public static IBridgeEngineTiming Fast()
    {
        return BridgeEngineTiming_Factory.Create_Custom(
            TICK_MILLISECONDS, AGGREGATION_SECONDS, RETRY_BACKOFF_SECONDS, TICK_LOCK_ALLOWANCE_MILLISECONDS,
            TRAILING_ENTRY_QUIET_MILLISECONDS);
    }

    /// <summary>
    /// Same as <see cref="Fast"/> but with the retry backoff named by the caller. For the one test
    /// whose subject IS the backoff.
    /// </summary>
    public static IBridgeEngineTiming Fast_WithRetryBackoff(int retryBackoffSeconds)
    {
        return BridgeEngineTiming_Factory.Create_Custom(
            TICK_MILLISECONDS, AGGREGATION_SECONDS, retryBackoffSeconds, TICK_LOCK_ALLOWANCE_MILLISECONDS,
            TRAILING_ENTRY_QUIET_MILLISECONDS);
    }

    /// <summary>
    /// A wall-clock window that certainly contains <paramref name="ticks"/> mirror ticks of a freshly
    /// started engine. Use it for both shapes of fixed wait: "let the tailer see the files" and
    /// "prove this never arrives".
    /// </summary>
    public static int Window_ForTicks(int ticks)
    {
        return STARTUP_MILLISECONDS + (ticks * TICK_MILLISECONDS);
    }

    /// <summary>
    /// A window that certainly contains <paramref name="ticks"/> ticks each of which spends its WHOLE
    /// lock allowance — for the tests that hold a channel locked and need several refused attempts.
    /// </summary>
    public static int Window_ForBlockedTicks(int ticks)
    {
        return STARTUP_MILLISECONDS + (ticks * (TICK_MILLISECONDS + TICK_LOCK_ALLOWANCE_MILLISECONDS));
    }

    /// <summary>
    /// A window that certainly contains a full owner-aggregation flush plus <paramref name="ticks"/>
    /// ticks — for the waits whose subject is the buffer rather than the tailer.
    /// </summary>
    public static int Window_ForAggregation(int ticks)
    {
        return Window_ForTicks(ticks) + (AGGREGATION_SECONDS * 1000);
    }
}
