namespace AIOrchestratorCoreLib.Bridge.ChannelChangeWaker;

public static class ChannelChangeWaker_Factory
{
    /// <summary>
    /// How long a wake waits for the rest of its burst before the loop runs. One append raises several
    /// events (a size change and a write stamp are two notifications for one <c>AppendAllText</c>), and
    /// a crew filing four reports at once is ONE arrival, not four ticks.
    /// </summary>
    public const int DEBOUNCE_MILLISECONDS = 150;

    /// <summary>
    /// The gap between the short polls that follow a wake — see <see cref="SETTLING_PULSES"/>.
    /// </summary>
    public const int SETTLING_PULSE_MILLISECONDS = 200;

    /// <summary>
    /// WHY A WAKE IS NOT ONE POLL. An arrival is rarely one write: a crew files four reports inside a
    /// second, a session appends a report and its supervisor answers it, and the debounce above folds
    /// all of that into ONE wake. The polls that follow are how the rest of the burst is read on the
    /// arrival that caused it rather than on the next tick — and, in particular, how an entry whose
    /// successor's header has just landed goes out at once instead of two seconds later.
    ///
    /// <para>
    /// THEY ARE NOT WHAT RELEASES A TRAILING ENTRY, and until 2026-09-09 this comment said they were.
    /// The tailer holds a channel's LAST entry until the file has stopped growing, because an entry
    /// still being written must never be mirrored half-formed and its tail silently dropped; that rule
    /// used to be "two polls with no growth", so these pulses appeared to serve it — and in serving it
    /// at 200 ms they cut a 4000 ms protection to ~350 ms. It is a DURATION now
    /// (<c>ChannelTailer_Factory.TRAILING_ENTRY_QUIET_MILLISECONDS</c>), which no poll cadence can move,
    /// and a trailing entry therefore reaches the phone on a later poll no matter how many pulses run.
    /// </para>
    /// <para>
    /// WHAT THE LOOP COSTS BECAUSE OF THEM, measured on this machine on 2026-09-09 and written here
    /// because this is where the next reader will be standing: under continuous appends the mirror loop
    /// polled 5.4 times a second against 0.5 before the waker. That is the price of the reaction and it
    /// was paid on purpose — but EVERY poll stats and reads every channel file of every live
    /// orchestration, so the bill grows with the size of the crew rather than with the traffic. Adding a
    /// pulse, or shortening <see cref="SETTLING_PULSE_MILLISECONDS"/>, multiplies that number.
    /// </para>
    /// </summary>
    public const int SETTLING_PULSES = 2;

    /// <summary>
    /// What one arrival may cost the loop, however many events it raised: the wake and its settling
    /// polls, and nothing per event. A burst that cost one tick each would be the mirror loop spinning
    /// on its own writes.
    /// </summary>
    public const int MAXIMUM_WAKES_PER_BURST = 1 + SETTLING_PULSES;

    /// <summary>
    /// How often the waker checks that its watch still points at a live folder — see
    /// <c>ChannelChangeWakerModel.Check_WatchStillValid</c> for why a watch can stop pointing at one.
    /// A mirror tick, so the check can never cost more than the loop it protects: between wakes the
    /// waiting method is entered several times a second.
    /// </summary>
    public const int VALIDITY_CHECK_MILLISECONDS = 2000;

    /// <summary>
    /// <paramref name="logLine"/> is how a watcher that cannot be started, that loses events, or whose
    /// watch has to be re-armed says so — one line per condition, into the orchestration log, never
    /// Telegram (the owner can do nothing about an inotify limit) and never stderr, which nothing here
    /// reads. A watcher this machine refuses is NOT an error the bridge propagates: the loop keeps its
    /// tick and works exactly as it did before.
    /// </summary>
    public static IChannelChangeWaker Create(string supervisionRoot, Action<string> logLine)
    {
        return new ChannelChangeWakerModel(supervisionRoot, logLine);
    }

    /// <summary>
    /// THE WATCHER ITSELF, in one place, because the model builds one at construction AND again on every
    /// re-arm — two copies of these filters would be two watchers that notice different things.
    /// It is returned NOT enabled: arming is what asks the OS for a handle and is what fails.
    /// </summary>
    internal static FileSystemWatcher Create_Watcher(string supervisionRoot)
    {
        return new FileSystemWatcher(supervisionRoot, "*.md")
        {
            IncludeSubdirectories = true,

            // Size AND LastWrite: an append is a size change on every filesystem here, and a write
            // stamp on most — asking for both is what makes the wake independent of which one this
            // platform reports. FileName covers the channel that did not exist yet, which is the
            // first entry of every new orchestration and implementer.
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
    }
}
