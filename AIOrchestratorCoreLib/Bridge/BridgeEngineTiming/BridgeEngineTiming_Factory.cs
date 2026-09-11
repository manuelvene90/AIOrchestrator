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

    /// <summary>
    /// The owner often texts several messages in a row — quiet time before delivery as ONE entry, so a
    /// burst arrives on the session as one turn instead of one turn each.
    ///
    /// <para>
    /// FOUR SECONDS WAS TOO SHORT TO BE HELD: WAIT can only stop a message still in the buffer, and
    /// four seconds is less than it takes to realise you have more to say and type a word — measured on
    /// the owner's machine, a WAIT five seconds behind its message arrived after the take and stopped
    /// nothing. It went to eight. SIX, because the ⏸ button changed what the window has to be long
    /// enough FOR: with a tap sitting under the receipt the owner set it back down themselves
    /// (2026-08-15) — "with the button we can reduce the window".
    /// </para>
    /// <para>
    /// THREE, and the balance changed again on 2026-09-09 because the window is no longer served in
    /// full by everybody. Measured on the VPS that day: 11–12 s median from the owner's text to the
    /// entry landing in the supervisor's channel, six of them here — and the owner asked for the wait
    /// to shrink. A message that reads as plainly over now serves a SHORTER window rather than this one
    /// (<c>OwnerDeliveryBufferModel.FINISHED_MESSAGE_QUIET_SECONDS</c>, asked below the ⏸ check so a
    /// hold still stops everything), which leaves this number covering what it was always for: a burst
    /// of typing that has not finished yet.
    /// </para>
    /// <para>
    /// IT SKIPPED THE WINDOW ENTIRELY FOR ONE EVENING, and that is why the sentence above says
    /// "shorter" and not "no". A finished message taken on the first flush pass left the buffer in
    /// 150–2000 ms, which put it out of reach of the ⏸ button this number is sized around AND defeated
    /// the aggregation: measured, two finished messages two seconds apart bought TWO supervisor turns
    /// where the same two without full stops bought one, at roughly a million input tokens the turn.
    /// </para>
    /// </summary>
    const int OWNER_AGGREGATION_SECONDS = 3;

    /// <summary>
    /// Pause before re-sending a channel whose mirror send failed. The tailer re-emits an
    /// unconfirmed append on EVERY poll — that is what makes the retry possible — so without this
    /// the retry would be a 2-second hammer against an endpoint that is already failing, which is
    /// precisely the shape that earns a bot a server-side throttle.
    /// </summary>
    const int MIRROR_RETRY_BACKOFF_SECONDS = 30;

    /// <summary>THE SHIPPED NUMBERS. Every production caller gets these, and nothing else may.</summary>
    public static IBridgeEngineTiming Create_Production()
    {
        return new BridgeEngineTimingModel(
            MIRROR_TICK_MILLISECONDS,
            OWNER_AGGREGATION_SECONDS,
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
    /// </summary>
    public static IBridgeEngineTiming Create_Custom(
        int mirrorTickMilliseconds,
        int ownerAggregationSeconds,
        int mirrorRetryBackoffSeconds,
        int tickLockAllowanceMilliseconds,
        int trailingEntryQuietMilliseconds)
    {
        if (mirrorTickMilliseconds < 1)
            throw new ArgumentException($"mirrorTickMilliseconds must be >= 1, got {mirrorTickMilliseconds}");

        if (ownerAggregationSeconds < 1)
            throw new ArgumentException($"ownerAggregationSeconds must be >= 1, got {ownerAggregationSeconds}");

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
            ownerAggregationSeconds,
            mirrorRetryBackoffSeconds,
            tickLockAllowanceMilliseconds,
            trailingEntryQuietMilliseconds);
    }
}
