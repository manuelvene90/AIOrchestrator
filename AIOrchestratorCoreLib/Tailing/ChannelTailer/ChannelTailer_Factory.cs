using AIOrchestratorCoreLib.Time.Clock;

namespace AIOrchestratorCoreLib.Tailing.ChannelTailer;

public static class ChannelTailer_Factory
{
    /// <summary>
    /// HOW LONG A CHANNEL FILE MUST HAVE STOPPED GROWING BEFORE ITS LAST ENTRY IS RELEASED — the
    /// tailer's whole protection against mirroring an entry that is still being written.
    ///
    /// <para>
    /// FOUR SECONDS BECAUSE THAT IS WHAT IT ALWAYS WAS, and the point of writing it as a duration is
    /// that it stays four seconds. The rule used to be "two polls with no growth", so the protection it
    /// actually bought was two times whatever the mirror loop's poll interval happened to be. Nothing
    /// said so. On 2026-09-09 <c>ChannelChangeWaker</c> made the loop poll at 200 ms after an append
    /// instead of on the 2000 ms tick, and the guarantee fell from 4000 ms to ~350 ms in a change that
    /// never mentioned the tailer: swept against a real stalling writer, an entry survived a 300 ms
    /// pause and was torn at 400 ms.
    /// </para>
    /// <para>
    /// AND A TEAR IS NOT A LATE MIRROR. The remainder of a torn entry arrives with no header, so
    /// <c>Extract_CompleteEntries</c> reads it as noise and clears it — see the comment on
    /// <c>ITailerPollResult.HeldTrailingEntryFiles</c>, which calls that outcome strictly worse than a
    /// delay. The cost of being wrong in the two directions is not comparable, so the number is the
    /// conservative one it has always been.
    /// </para>
    /// <para>
    /// IT IS ONLY EVER THE TRAILING ENTRY'S BILL. An entry followed by a header is PROVEN complete and
    /// goes out on the very poll that reads it — which is what the waker made fast, and it stays fast.
    /// Pinned by <c>AStalledWriterNeverTearsATrailingEntryTests</c> (the guarantee) and
    /// <c>AChannelAppendWakesTheBridgeTests</c> (the fast path).
    /// </para>
    /// </summary>
    public const int TRAILING_ENTRY_QUIET_MILLISECONDS = 4000;

    /// <summary>The shipped tailer: the quiet period above, read against the wall clock.</summary>
    public static IChannelTailer Create(IReadOnlyDictionary<string, long> persistedOffsets)
    {
        return Create(persistedOffsets, TimeSpan.FromMilliseconds(TRAILING_ENTRY_QUIET_MILLISECONDS), Clock_Factory.Create_System());
    }

    /// <summary>
    /// THE SEAM, and it is two seams for two different kinds of caller. A test that drives the real
    /// bridge pays the quiet period in wall-clock sleep once per mirrored entry, so it hands in a
    /// shorter one (<c>BridgeTestTiming</c>); a test that drives <see cref="IChannelTailer.Poll"/>
    /// synchronously has no wall clock at all, so it hands in a clock it steps itself. Neither may
    /// reach production: <see cref="Create(IReadOnlyDictionary{string, long})"/> is what
    /// <c>BridgeEngine_Factory</c> resolves through the engine's timing.
    /// </summary>
    public static IChannelTailer Create(IReadOnlyDictionary<string, long> persistedOffsets, TimeSpan trailingEntryQuiet, IClock clock)
    {
        if (trailingEntryQuiet <= TimeSpan.Zero)
            throw new ArgumentException($"trailingEntryQuiet must be > 0, got {trailingEntryQuiet}");

        return new ChannelTailerModel(persistedOffsets, trailingEntryQuiet, clock);
    }

    public static IChannelTailer Create_Fresh()
    {
        return Create(new Dictionary<string, long>());
    }

    /// <inheritdoc cref="Create(IReadOnlyDictionary{string, long}, TimeSpan, IClock)"/>
    public static IChannelTailer Create_Fresh(TimeSpan trailingEntryQuiet, IClock clock)
    {
        return Create(new Dictionary<string, long>(), trailingEntryQuiet, clock);
    }
}
