using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.BridgeEngineTiming;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Diagnostics;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.Sessions;
using Xunit;
using Xunit.Abstractions;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// WHAT ONE MIRROR TICK COSTS THE DISK, over a supervision root the size of a real working day:
/// three orchestrations, three members each, every channel populated.
///
/// <para>
/// THE WASTE THIS PINS IS PURE REPETITION. Inside one 2-second tick the engine asked the store for
/// the whole roster from twenty-four places, offered every channel to the compactor (which read and
/// parsed all of them to discover they were nowhere near the 90-entry threshold), and re-read every
/// member's live-and-archive history from a dozen unrelated sweeps. Every one of those answers was
/// identical to the one before it, which is exactly why no behaviour test could see the difference:
/// the tick produced the same Telegram traffic, the same log and the same files either way. A count
/// is the only assertion that can tell one read from twenty-four.
/// </para>
/// <para>
/// THE NUMBERS ARE REPORTED, NOT ONLY ASSERTED. <see cref="OneTick_FileOpens_AreReported"/> prints
/// what the tick actually cost, so the next person to change this does not have to reconstruct the
/// measurement to know whether they made it worse. The assertions are ceilings with room in them —
/// a tick is not deterministic to the syscall (a topic gets created on the first tick, a status line
/// is composed once) — and they are set far below what the unfixed engine spends, so they fail on a
/// regression rather than on a slow machine.
/// </para>
/// <para>
/// EVERY FIGURE HERE IS PER TICK, AND IT HAS TO BE SAID because it was not always so. Until the
/// ChannelChangeWaker landed (2026-09-09) the inter-tick wait was a flat 2 s, so a count taken over a
/// wall-clock window WAS a count per tick and nothing distinguished the two. The waker also ends the
/// wait on a filesystem event, and its wake is followed by two 200 ms settling pulses — so a 3.5 s
/// window that used to hold one tick can hold four. This class read 176 against 78 and reported the
/// channel cache broken; the 176 were four ticks of 44. Every reading below therefore carries
/// <see cref="TickIo_Counters.Counts.TicksEntered"/> and every assertion divides by it.
/// </para>
/// <para>
/// THE FIRST TICK IS MEASURED BY CANCELLING ON ITS LAST STATEMENT — <c>Persist_BridgeState</c> writing
/// the cursor file — and the reading is VOID unless exactly one tick was entered, which is asserted
/// rather than assumed. The soonest a second tick can follow is the waker's 150 ms debounce and the
/// detection loop polls at 5 ms, so the margin is thirtyfold; a machine slow enough to lose it fails
/// with "this reading covers N ticks" instead of with a wrong number.
/// </para>
/// <para>
/// THE STEADY-STATE TICK IS MEASURED BY DELTA between two readings taken while nothing is running —
/// see <see cref="Sample_WhenQuiet_Async"/> — divided by the ticks between them. That is the only
/// shape immune to the waker: whatever ends the wait, and however many ticks fit in the window, the
/// quotient is what one tick cost.
/// </para>
/// </summary>
public class TheTickDoesNotRepeatItselfTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;

    const int ORCHESTRATIONS = 3;
    const int MEMBERS_PER_ORCHESTRATION = 3;

    /// <summary>Short of the compaction threshold (90) on purpose: that is the shape of a live channel.</summary>
    const int ENTRIES_PER_CHANNEL = 40;

    /// <summary>
    /// How many whole ticks the steady-state reading is averaged over. Five, because a quiet sample can
    /// in principle land inside a tick that happens to pause its IO for the length of the stability
    /// window, and averaging over five caps what one such misattribution can do to the quotient at a
    /// fifth of a tick. Fewer would be sharper on the clock and blunter on the number; more would spend
    /// wall-clock for precision the assertion does not need — the gap it has to see is 44 against 102.
    /// </summary>
    const int STEADY_STATE_TICKS_MEASURED = 5;

    /// <summary>
    /// A reading is taken only after the counters have stood still for this long, which is how a sample
    /// is known to fall between ticks rather than inside one. Comfortably longer than a tick over this
    /// root (~30 ms end to end) and comfortably shorter than the 2 s a quiet tick waits.
    /// </summary>
    const int QUIET_MILLISECONDS = 120;

    readonly ITestOutputHelper _output;
    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationLauncher _launcher;
    readonly LoadAllCounting_Store_Fake _store;
    readonly IBridgeEngine _engine;

    public TheTickDoesNotRepeatItselfTests(ITestOutputHelper output)
    {
        _output = output;
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-tick-cost-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        // The inbound loop needs the chat and owner ids. The Italian layer is pinned OFF because it
        // defaults ON and would reach the real translator.
        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID},\"telegramItalianLayer\":false}}");

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = new LoadAllCounting_Store_Fake(OrchestrationSessionStore_Factory.Create(_paths));

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var log = OrchestrationLog_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), log);

        // A Telegram client is what makes the tick do its real work: half the sweeps return
        // immediately when there is none, and the status-line pass — the heaviest reader of channel
        // history — is one of them. A file-only engine would measure a tick nobody runs.
        _engine = BridgeEngine_Factory.Create_WithTelegramClient(
            _paths, configProvider, _store, _launcher, log, new FailableTelegram_Fake(),
            BridgeEngineTiming_Factory.Create_Production());

        Seed_SupervisionRoot();
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// THE MEASUREMENT. Not an assertion about a target number — those move — but the two ceilings
    /// that separate "read once and reused" from "read again by every asker".
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task OneTick_FileOpens_AreReported()
    {
        var counts = await Run_OneTick_Async();

        _output.WriteLine(
            $"ONE MIRROR TICK over {ORCHESTRATIONS} orchestrations x {MEMBERS_PER_ORCHESTRATION} members, "
            + $"{ENTRIES_PER_CHANNEL} entries per channel: Load_All calls={_store.LoadAll_Calls}, {counts}");

        // WITHOUT THIS THE REST IS NOT A TICK'S COST. See the class remark: the wait ends on a
        // filesystem event as well as on the clock, so "the counts when the cursor file appeared" is
        // one tick only while no second tick has started, and that is a fact to check, not to assume.
        Assert.True(
            counts.Ticks == 1,
            $"this reading covers {counts.Ticks} ticks, not one, so the numbers below are a window and "
            + "not a tick's cost. The first tick's end was detected too late for the wake that follows it.");

        // MEASURED ON THIS BRANCH, 2026-09-09, this scenario, WITH THE ChannelChangeWaker PRESENT and
        // the same seam on both sides — the before figures come from a copy of this tree with P7's five
        // caches switched off (the roster snapshot, the session-json parse cache, the compaction gate,
        // the channel-history cache, the cursor comparison) and nothing else changed. THE FIRST TICK:
        //   before: Load_All=17, session.json reads=96, text-file reads=115
        //   after:  Load_All=5,  session.json reads=0,  text-file reads=78
        // The ceilings below sit between the two, so each of them fails on a regression rather than
        // on a slow machine.

        // The roster: the tick takes ONE. What is left over are the passes that legitimately read the
        // disk outside the snapshot — the plan-backend pass runs on its own task and outlives the
        // tick, the watchdog runs on its own loop, and the general dashboard's report is also a
        // Telegram command handler, so none of the three may be handed the tick's snapshot.
        Assert.InRange(_store.LoadAll_Calls, 1, 8);

        // session.json: none at all here, because the roster was already parsed while this test
        // seeded the root and nothing has changed it since. That is the app's normal state — it is a
        // long-running process — and it is what the unfixed engine spent 96 reads on.
        Assert.InRange(counts.SessionFileReads, 0, 15);

        Assert.InRange(counts.TextFileReads, 1, 95);

        // The cursor is written at the end of the tick — the first tick baselines every channel, so
        // it genuinely changed — and again, forced, when the run is cancelled.
        Assert.InRange(counts.BridgeStateWrites, 1, 3);
    }

    /// <summary>
    /// A STEADY-STATE TICK OVER AN UNCHANGED ROOT MUST BE FAR CHEAPER THAN THE FIRST, and must not
    /// write the cursor at all. This is the assertion that actually pins the caches: the first tick has
    /// to read everything whatever the caching, so a fixed engine and an unfixed one are hard to tell
    /// apart there. Later ticks are where they diverge — nothing on disk changed, so a tick that still
    /// reads the channels is reading them for no reason, and a tick that still writes the cursor is
    /// rewriting bytes identical to the ones already in the file.
    ///
    /// <para>
    /// PER TICK, NOT PER WINDOW. This test asserted on a 3.5-second window until 2026-09-09 and the
    /// ChannelChangeWaker broke it the day it merged — not by costing anything, but by fitting four
    /// ticks where the window had held one. The number it then reported, "176 against 78", was four
    /// correct ticks of 44 read as one broken tick of 176. The window is now divided by the ticks in it.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ASteadyStateTickOverAnUnchangedRoot_ReadsFarLessThanTheFirst_AndWritesNoCursor()
    {
        var (firstTick, steadyState, shutdown) = await Run_FirstTick_ThenSteadyState_Async();

        _output.WriteLine($"TICK 1 (cold): {firstTick}");
        _output.WriteLine($"STEADY STATE, nothing changed on disk: {steadyState}");
        _output.WriteLine($"  per tick: {steadyState.Per_Tick()}");
        _output.WriteLine($"AFTER SHUTDOWN: {shutdown}");

        Assert.True(
            firstTick.Ticks == 1,
            $"the cold reading covers {firstTick.Ticks} ticks, not one — it cannot be compared against a tick.");

        Assert.True(
            steadyState.Ticks >= STEADY_STATE_TICKS_MEASURED,
            $"only {steadyState.Ticks} ticks were measured; the quotient below would be noise.");

        var perTick = steadyState.Per_Tick();

        // MEASURED, 2026-09-09, with the waker present, same tree with P7's caches switched off: a
        // STEADY-STATE tick costs 96 session.json reads, 102 text-file reads and 1 cursor write — 198
        // file opens and a write. With them on it costs 0, 44 and 0. The ceilings sit between the two.
        // (The 195 in this branch's commit message was the same measurement taken before the waker
        // merged; the waker does not change what a tick costs, only when it runs — the after figure is
        // 44 whether the wait ends on the clock or on a filesystem event — and the three extra opens
        // came in with the merge, not with the waker.)
        Assert.True(
            perTick.TextFileReads < firstTick.TextFileReads,
            $"a steady-state tick read as much as the cold first one ({perTick.TextFileReads} vs "
            + $"{firstTick.TextFileReads}) over a root that did not change — the channel parses are not "
            + "being reused across ticks.");

        Assert.InRange(perTick.TextFileReads, 0, 70);
        Assert.InRange(perTick.SessionFileReads, 0, 15);

        // D, THE ASSERTION THAT COSTS THE WRITE: nothing on disk moved, so the cursor says exactly
        // what the file already says, so the file is not rewritten — not once in the whole window,
        // which is a stronger statement than a per-tick average and the one that is actually true.
        // The unfixed engine rewrote it every tick.
        Assert.Equal(0, steadyState.BridgeStateWrites);

        // AND THE EXCEPTION TO IT. The last write of the process is forced, because a remembered
        // cursor that has drifted from the file has no later tick left to correct it.
        Assert.True(
            shutdown.BridgeStateWrites > 0,
            "the bridge cursor was not written on shutdown. Every tick skipping an unchanged cursor is "
            + "only safe while the last word belongs to a forced write.");
    }

    /// <summary>
    /// THE BRIDGE MUST NOT WAKE ITSELF IN A CIRCLE. The ChannelChangeWaker ends the mirror loop's wait
    /// on any <c>*.md</c> write under the supervision root — and the bridge writes <c>.md</c> files of
    /// its own (an app entry into a channel, a compaction moving half of one into its archive). Each
    /// such write costs a wake plus two settling pulses, which is correct while it terminates: what the
    /// app wrote into a channel is exactly what has to reach Telegram, and the pulses are what release
    /// it from the tailer's quiet-poll gate. It stops terminating the moment some tick starts writing
    /// an <c>.md</c> unconditionally — then every tick wakes the next one at the 150 ms debounce
    /// instead of the 2 s tick, and an idle box spins at ten times the intended rate, multiplying every
    /// per-tick cost this class exists to cut.
    ///
    /// <para>
    /// MEASURED, 2026-09-09, over this root: 60 s idle gives 30 ticks with the waker and 30 without —
    /// the cadence is 2.0 s in both, and the only difference over a short window is the one-off burst
    /// the engine's own first-tick writes to <c>general/channel.md</c> raise at startup. So the ceiling
    /// below is not a tolerance for a loop that half-exists; it is the tick rate, with room for one
    /// burst.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AnIdleRoot_IsNotTickedFasterThanTheTick()
    {
        const int WINDOW_MILLISECONDS = 12_000;

        using var counting = TickIo_Counters.Begin_Scope();
        using var cancellation = new CancellationTokenSource();

        var loop = _engine.Run_Async(cancellation.Token);

        await Wait_For_FirstTickEnd_Async();

        // AFTER the startup burst, not across it: the engine writes the general channel into existence
        // on its first tick and that write is a legitimate wake. What this measures is the rate once
        // there is nothing left to react to.
        await Task.Delay(2_000);

        var before = await Sample_WhenQuiet_Async(counting.Counts);

        await Task.Delay(WINDOW_MILLISECONDS);

        var after = await Sample_WhenQuiet_Async(counting.Counts);

        await cancellation.CancelAsync();
        await Await_Loop_Async(loop);

        var window = after.Minus(before);

        _output.WriteLine($"{WINDOW_MILLISECONDS} ms idle: {window}");

        var ceiling = (WINDOW_MILLISECONDS / BridgeEngineTiming_Factory.Create_Production().MirrorTickMilliseconds) + 3;

        Assert.True(
            window.Ticks <= ceiling,
            $"an idle root was ticked {window.Ticks} times in {WINDOW_MILLISECONDS} ms, over a ceiling of "
            + $"{ceiling}. Something the tick writes is waking the tick: the loop is spinning on its own "
            + "output, which costs CPU on a box with nothing to do and multiplies every per-tick read.");
    }

    async Task<Reading> Run_OneTick_Async()
    {
        using var counting = TickIo_Counters.Begin_Scope();
        using var cancellation = new CancellationTokenSource();

        var loop = _engine.Run_Async(cancellation.Token);

        await Wait_For_FirstTickEnd_Async();
        await cancellation.CancelAsync();
        await Await_Loop_Async(loop);

        return Reading.Of(counting.Counts);
    }

    async Task<(Reading FirstTick, Reading SteadyState, Reading AfterShutdown)> Run_FirstTick_ThenSteadyState_Async()
    {
        using var counting = TickIo_Counters.Begin_Scope();
        using var cancellation = new CancellationTokenSource();

        var loop = _engine.Run_Async(cancellation.Token);

        await Wait_For_FirstTickEnd_Async();

        var firstTick = Reading.Of(counting.Counts);

        // FROM A STANDSTILL TO A STANDSTILL, so the delta between them covers whole ticks and nothing
        // else. Where the ticks fall inside that span is the waker's business, not this test's.
        var before = await Sample_WhenQuiet_Async(counting.Counts);

        await Wait_For_Tick_Async(counting.Counts, before.Ticks + STEADY_STATE_TICKS_MEASURED);

        var after = await Sample_WhenQuiet_Async(counting.Counts);

        var beforeShutdown = Reading.Of(counting.Counts);

        await cancellation.CancelAsync();
        await Await_Loop_Async(loop);

        return (firstTick, after.Minus(before), Reading.Of(counting.Counts).Minus(beforeShutdown));
    }

    /// <summary>
    /// Waits for the cursor file to be written, which is the mirror tick's last statement. Used only
    /// for the FIRST tick, whose baselining always changes the cursor. Polls at 5 ms because the
    /// soonest a second tick can follow is the waker's 150 ms debounce, and a reading that catches the
    /// second tick is not a reading of the first.
    /// </summary>
    async Task Wait_For_FirstTickEnd_Async()
    {
        for (var waited = 0; waited < 20_000; waited += 5)
        {
            if (File.Exists(_paths.BridgeStateFile))
                return;

            await Task.Delay(5);
        }

        throw new Exception(
            "no mirror tick ever reached its last statement, so nothing was measured. The counts below "
            + "would be an empty run reported as a saving.");
    }

    /// <summary>Waits until the loop has entered at least <paramref name="ticks"/> ticks.</summary>
    static async Task Wait_For_Tick_Async(TickIo_Counters.Counts live, long ticks)
    {
        for (var waited = 0; waited < 60_000; waited += 25)
        {
            if (live.TicksEntered >= ticks)
                return;

            await Task.Delay(25);
        }

        throw new Exception(
            $"the mirror loop never reached tick {ticks}. A reading divided by the ticks that did happen "
            + "would be an average over too few of them to mean anything.");
    }

    /// <summary>
    /// A reading taken while nothing is running: the counters are watched until they have not moved for
    /// <see cref="QUIET_MILLISECONDS"/>, which over this root means the last tick has finished and the
    /// next has not started. Two such readings therefore differ by whole ticks, and dividing one by the
    /// other's tick count is a per-tick cost rather than a per-window one.
    /// </summary>
    static async Task<Reading> Sample_WhenQuiet_Async(TickIo_Counters.Counts live)
    {
        var previous = Reading.Of(live);

        for (var waited = 0; waited < 30_000; waited += QUIET_MILLISECONDS)
        {
            await Task.Delay(QUIET_MILLISECONDS);

            var current = Reading.Of(live);

            if (current == previous)
                return current;

            previous = current;
        }

        throw new Exception(
            "the counters never stood still, so no reading could be attributed to whole ticks. Either "
            + "the mirror loop is spinning or something outside it is reading this root.");
    }

    /// <summary>
    /// A FROZEN COPY of the live counts. The scope's counter object keeps counting — that is what
    /// makes a background loop's syscalls visible — so a second reading taken later would otherwise
    /// be the same object as the first and the two would compare equal whatever happened.
    /// </summary>
    readonly record struct Reading(long Ticks, long SessionFileReads, long TextFileReads, long BridgeStateWrites)
    {
        public static Reading Of(TickIo_Counters.Counts live)
        {
            // TICKS FIRST, AND THAT ORDER IS DELIBERATE. Read last, it could name a tick whose reads are
            // already in the numbers beside it, and the quotient would divide a window by one tick too
            // many. Read first, the worst case is a tick counted in the reads and not in the divisor,
            // which overstates the cost — a reading that errs towards failing this class's ceilings.
            return new Reading(
                live.TicksEntered, live.SessionFileReads, live.TextFileReads, live.BridgeStateWrites);
        }

        /// <summary>This reading less an earlier one: what happened between the two.</summary>
        public Reading Minus(Reading earlier)
        {
            return new Reading(
                Ticks - earlier.Ticks,
                SessionFileReads - earlier.SessionFileReads,
                TextFileReads - earlier.TextFileReads,
                BridgeStateWrites - earlier.BridgeStateWrites);
        }

        /// <summary>
        /// This window averaged over the ticks in it — the only figure comparable across a change to
        /// what ends the inter-tick wait. Ticks of zero would be a window that measured nothing, and it
        /// is reported as itself rather than divided by one, which would read as a very cheap tick.
        /// </summary>
        public Reading Per_Tick()
        {
            if (Ticks <= 0)
                return this;

            return new Reading(
                1, SessionFileReads / Ticks, TextFileReads / Ticks, BridgeStateWrites / Ticks);
        }

        public override string ToString()
        {
            return $"ticks={Ticks}, session.json reads={SessionFileReads}, text-file reads={TextFileReads}, "
                + $"bridge-state writes={BridgeStateWrites}";
        }
    }

    static async Task Await_Loop_Async(Task loop)
    {
        try
        {
            await loop;
        }
        catch (OperationCanceledException)
        {
            // The only way these loops end.
        }
    }

    /// <summary>
    /// Three orchestrations, each with a supervisor and two implementers, each channel carrying a
    /// working day's traffic and a PLAN.md beside it. This is the shape the tick's cost scales with.
    /// </summary>
    void Seed_SupervisionRoot()
    {
        for (var orchestration = 0; orchestration < ORCHESTRATIONS; orchestration++)
        {
            var session = _launcher.Start_Orchestration($"Repo{orchestration}", _tempRepo);

            while (session.Members.Count < MEMBERS_PER_ORCHESTRATION)
                session = _launcher.Add_Implementer(session.OrchId);

            Write_Channel(_paths.Get_OwnerChannelFile(session.OrchId));

            foreach (var member in session.Members)
                Write_Channel(_paths.Get_ImplementerChannelFile(session.OrchId, member.MemberId));
        }
    }

    static void Write_Channel(string channelFile)
    {
        var text = string.Concat(Enumerable.Range(1, ENTRIES_PER_CHANNEL).Select(index =>
            $"## [{index}] FROM {(index % 2 == 0 ? "supervisor" : "implementer")} — 2026-09-09 10:{index:D2} — entry {index}\n"
            + $"body of entry {index}, long enough to be worth reading twice.\n\n"));

        File.WriteAllText(channelFile, text);
    }
}
