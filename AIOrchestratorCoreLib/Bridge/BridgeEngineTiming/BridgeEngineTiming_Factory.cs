using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Tailing.ChannelTailer;

namespace AIOrchestratorCoreLib.Bridge.BridgeEngineTiming;

public static class BridgeEngineTiming_Factory
{
    /// <summary>
    /// THE CEILING ON HOW LONG AN APPEND CAN GO UNNOTICED — not the rate the loop runs at, which is
    /// the distinction the whole of the rest of this comment turns on.
    ///
    /// <para>
    /// It stays 2000 because it is the SAFETY NET: <see cref="ChannelChangeWaker.IChannelChangeWaker"/>
    /// ends the wait early when the filesystem says a channel was written, and filesystem notification
    /// is best-effort everywhere (inotify runs out of watches on a Linux box with many folders, a
    /// network filesystem reports nothing, a container can have no backend at all). Where the watcher
    /// never fires, the loop behaves EXACTLY as it did before it existed.
    /// </para>
    /// <para>
    /// SO THIS IS NOT THE POLL RATE. What the loop actually costs under continuous appends — measured,
    /// and a different number entirely — is recorded next to the constants that decide it, on
    /// <c>ChannelChangeWaker_Factory.SETTLING_PULSES</c>. One copy, there.
    /// </para>
    /// </summary>
    const int MIRROR_TICK_MILLISECONDS = 2000;

    // THERE IS NO OWNER_AGGREGATION_SECONDS HERE ANY MORE (2026-09-23, plan 03 task 13). The window is
    // the owner's setting, phone.aggregationSeconds; its shipped value and its whole 4 → 8 → 6 → 3 history
    // moved to OwnerDeliveryBuffer_Factory.DEFAULT_AGGREGATION_SECONDS, which the catalogue row reads.

    /// <summary>
    /// Pause before re-sending a channel whose mirror send failed. The tailer re-emits an
    /// unconfirmed append on EVERY poll — that is what makes the retry possible — so without this
    /// the retry would be a 2-second hammer against an endpoint that is already failing, which is
    /// precisely the shape that earns a bot a server-side throttle.
    /// </summary>
    const int MIRROR_RETRY_BACKOFF_SECONDS = 30;

    /// <summary>
    /// THE SHIPPED NUMBERS. Every production caller gets these, and nothing else may.
    ///
    /// <para>
    /// AND NO AGGREGATION WINDOW — the one period production does not name here (2026-09-23, plan 03 task
    /// 13). The window is a per-user setting, so production timing carries null and the engine's flush
    /// resolves <c>phone.aggregationSeconds</c> / <c>phone.finishedMessageSeconds</c> from
    /// <c>_configProvider.Get_Current().Phone</c> on every pass (<c>OwnerAggregationWindow_Resolver</c>).
    /// THE SEAM'S DESIGN, in one sentence: <see cref="Create_Production"/> carries no window and
    /// <see cref="Create_Custom"/> carries one, and a carried window outranks the setting — so every
    /// engine test that hands in a one-second or sixty-second window keeps it, unedited, while
    /// <see cref="Create_Custom_WindowFromSettings"/> is the fast-tick seam for the tests whose subject IS
    /// the setting.
    /// </para>
    /// </summary>
    public static IBridgeEngineTiming Create_Production()
    {
        return new BridgeEngineTimingModel(
            MIRROR_TICK_MILLISECONDS,
            ownerAggregationSecondsOrNull: null,
            MIRROR_RETRY_BACKOFF_SECONDS,
            (int)ChannelWrite_Lock.DEFAULT_TICK_ALLOWANCE.TotalMilliseconds,
            ChannelTailer_Factory.TRAILING_ENTRY_QUIET_MILLISECONDS);
    }

    /// <summary>
    /// THE TEST SEAM. A test that drives the real engine loop pays these periods in wall-clock
    /// sleep; handing in shorter ones buys the run back without changing a single thing the test
    /// asserts. The guards below are the production floors the rest of the engine relies on: a
    /// non-positive tick is a hot loop, and <c>OwnerDeliveryBuffer_Factory</c> refuses an
    /// aggregation window under a second.
    ///
    /// <para>
    /// <paramref name="ownerAggregationSeconds"/> OUTRANKS the owner's <c>phone.aggregationSeconds</c> —
    /// see <see cref="Create_Production"/> for why that is the seam's design.
    /// </para>
    /// </summary>
    public static IBridgeEngineTiming Create_Custom(
        int mirrorTickMilliseconds,
        int ownerAggregationSeconds,
        int mirrorRetryBackoffSeconds,
        int tickLockAllowanceMilliseconds,
        int trailingEntryQuietMilliseconds)
    {
        if (ownerAggregationSeconds < 1)
            throw new ArgumentException($"ownerAggregationSeconds must be >= 1, got {ownerAggregationSeconds}");

        return Create_Validated(
            mirrorTickMilliseconds, ownerAggregationSeconds, mirrorRetryBackoffSeconds, tickLockAllowanceMilliseconds,
            trailingEntryQuietMilliseconds);
    }

    /// <summary>
    /// THE TEST SEAM WITH THE OWNER'S WINDOW — fast ticks, and NO aggregation window, exactly as
    /// production carries none, so the engine resolves <c>phone.aggregationSeconds</c> from config.json
    /// on every flush. For the tests whose subject is that setting (plan 03 task 13): a window named
    /// here would outrank the very thing they measure.
    /// </summary>
    public static IBridgeEngineTiming Create_Custom_WindowFromSettings(
        int mirrorTickMilliseconds,
        int mirrorRetryBackoffSeconds,
        int tickLockAllowanceMilliseconds,
        int trailingEntryQuietMilliseconds)
    {
        return Create_Validated(
            mirrorTickMilliseconds, ownerAggregationSecondsOrNull: null, mirrorRetryBackoffSeconds,
            tickLockAllowanceMilliseconds, trailingEntryQuietMilliseconds);
    }

    /// <summary>The production floors both test seams are held to — one copy of the guards.</summary>
    static IBridgeEngineTiming Create_Validated(
        int mirrorTickMilliseconds,
        int? ownerAggregationSecondsOrNull,
        int mirrorRetryBackoffSeconds,
        int tickLockAllowanceMilliseconds,
        int trailingEntryQuietMilliseconds)
    {
        if (mirrorTickMilliseconds < 1)
            throw new ArgumentException($"mirrorTickMilliseconds must be >= 1, got {mirrorTickMilliseconds}");

        if (mirrorRetryBackoffSeconds < 1)
            throw new ArgumentException($"mirrorRetryBackoffSeconds must be >= 1, got {mirrorRetryBackoffSeconds}");

        // ONE, NOT ZERO. An allowance of zero is still an allowance in force, so the guard is not
        // about the mechanism — it is that a tick which may not wait at all cannot distinguish a
        // contended channel from a free one on a loaded machine, and every lock test in the suite
        // would start passing for the wrong reason.
        if (tickLockAllowanceMilliseconds < 1)
            throw new ArgumentException($"tickLockAllowanceMilliseconds must be >= 1, got {tickLockAllowanceMilliseconds}");

        // ONE, NOT ZERO, and for the same reason as the allowance above: a quiet period of zero means
        // the tailer releases a trailing entry the instant it reads it, which is the torn-entry defect
        // of 2026-09-09 (ChannelTailer_Factory.TRAILING_ENTRY_QUIET_MILLISECONDS) written down as a
        // configuration rather than reached by accident.
        if (trailingEntryQuietMilliseconds < 1)
            throw new ArgumentException($"trailingEntryQuietMilliseconds must be >= 1, got {trailingEntryQuietMilliseconds}");

        return new BridgeEngineTimingModel(
            mirrorTickMilliseconds,
            ownerAggregationSecondsOrNull,
            mirrorRetryBackoffSeconds,
            tickLockAllowanceMilliseconds,
            trailingEntryQuietMilliseconds);
    }
}
