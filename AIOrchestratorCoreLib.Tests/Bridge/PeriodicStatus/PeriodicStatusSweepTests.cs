using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Bridge.PeriodicStatus;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Configuration.PhoneSettings;
using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Planning.PlanProgress;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.PeriodicStatus;

/// <summary>
/// THE PERIODIC STATUS, BACK UNDER A SETTING — and the away digest beside it, unmoved (plan 03 Task 8).
///
/// <para>
/// The owner, 2026-09-23: *"he removed the status message, but I liked it, so we should be able to opt
/// in."* Answer D1 (a): re-port master's status under <c>phone.status.periodic</c> — shipped and classic
/// <c>true</c>, quiet <c>false</c>, every 30 minutes by default. Answer D9: that setting moves the
/// periodic status only; the away digest keeps <see cref="PeriodicStatusSlot_Planner.SLOT_MINUTES"/>.
/// Ruling R6: the no-change guard applies to the re-ported status too, because the complaint that got
/// it deleted (2026-09-09) was three IDENTICAL messages at 19:00, 19:30 and 20:00.
/// </para>
/// <para>
/// BEHAVIOURAL, FOR THE FIRST TIME. Until this task the sweep was a private method of
/// <c>BridgeEngineModel</c>, and every claim about it was a source scan that could only prove a gate
/// was PRESENT. Here the sweep is driven over a grid of 2-second ticks — the bridge's real tick — with
/// an explicit clock and a stub host, so what it POSTS is the oracle. The phone block is resolved the
/// way production resolves it: a real config.json (or none) on a temp supervision root, through
/// <see cref="OrchestratorConfig_Loader"/>, so "under classic" means classic.json as shipped.
/// </para>
/// </summary>
public class PeriodicStatusSweepTests : IDisposable
{
    static readonly DateTime NOON = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Local);

    const int TICK_SECONDS = 2;

    const string LEDGER_WITH_WORK_IN_FLIGHT = """
        # PLAN

        - [x] 1. map the legacy behaviour
        - [>] 2. implement the token exchange
        - [ ] 3. wire the UI
        """;

    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public PeriodicStatusSweepTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-periodic-status-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    // ---------------------------------------------------------------------------------------------
    // The setting, per preset
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// QUIET IS NATHAN'S PHONE, and it never gets one. Everything that would make a status go out is
    /// arranged — work in flight, a status that changes on every tick, four hours of boundaries — so an
    /// empty list can only mean the setting held, not that nothing was due.
    /// </summary>
    [Fact]
    public async Task UnderQuiet_NoPeriodicStatusIsEverSent()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"preset":"quiet"}""");
        var phone = Load_Phone();
        Assert.False(phone.PeriodicStatus);

        var host = new StubHost { StatusChangesOnEveryRead = true };

        await Drive_Async(phone, host, NOON.AddMinutes(7), NOON.AddHours(4), isAway: false);

        Assert.Empty(host.Posts);

        // Nothing was even BUILT — an off setting costs no channel reads on any tick.
        Assert.Equal(0, host.MemberStatusBuilds);
    }

    /// <summary>
    /// CLASSIC IS MANU'S PHONE, and a machine that names no preset is classic. The status goes out on
    /// every half-hour boundary the orchestration was present for — never on first sight (a restart
    /// must not push every topic at once, off-boundary) — and it is master's text: the STATUS header,
    /// the roster /status answers with, and the ledger's current task.
    /// </summary>
    [Fact]
    public async Task UnderClassic_ThePeriodicStatusIsPostedOnItsSlot_WhenTheStatusChanged()
    {
        var phone = Load_Phone();
        Assert.True(phone.PeriodicStatus);
        Assert.Equal(30, phone.PeriodicStatusIntervalMinutes);

        var host = new StubHost { StatusChangesOnEveryRead = true };

        await Drive_Async(phone, host, NOON.AddMinutes(7), NOON.AddHours(2).AddMinutes(10), isAway: false);

        Assert.Equal(
            [NOON.AddMinutes(30), NOON.AddMinutes(60), NOON.AddMinutes(90), NOON.AddMinutes(120)],
            [.. host.Posts.Select(post => post.At)]);

        var first = host.Posts[0].Text;

        Assert.StartsWith($"{PeriodicStatus_Builder.HEADER}\n", first, StringComparison.Ordinal);
        Assert.Contains("- now: implement the token exchange", first, StringComparison.Ordinal);

        // The owner reads the version WITH the elapsed readings; only the guard compares without them.
        Assert.Contains("last wrote", first, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE AWAY DIGEST DOES NOT MOVE (D9). One planner governed both and AwayDigest_Decider's own
    /// docstring records a limit cycle locked to exactly SLOT_MINUTES. Changing the status interval
    /// must leave the away cadence where it is, and the two numbers must be visibly different values
    /// rather than accidentally the same one.
    /// </summary>
    [Fact]
    public async Task ChangingTheStatusInterval_LeavesTheAwayDigestOnItsOwnSlot()
    {
        const int STATUS_INTERVAL = 20;

        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"phone":{"status":{"intervalMinutes":""" + STATUS_INTERVAL + "}}}");
        var phone = Load_Phone();

        // Two different numbers, or this test proves nothing about which one each cadence obeys.
        Assert.Equal(STATUS_INTERVAL, phone.PeriodicStatusIntervalMinutes);
        Assert.NotEqual(PeriodicStatusSlot_Planner.SLOT_MINUTES, phone.PeriodicStatusIntervalMinutes);

        var present = new StubHost { StatusChangesOnEveryRead = true };
        await Drive_Async(phone, present, NOON.AddMinutes(7), NOON.AddHours(2).AddMinutes(10), isAway: false);

        Assert.Equal(
            [NOON.AddMinutes(20), NOON.AddMinutes(40), NOON.AddMinutes(60), NOON.AddMinutes(80), NOON.AddMinutes(100), NOON.AddMinutes(120)],
            [.. present.Posts.Select(post => post.At)]);

        var away = new StubHost { AwayDigestChangesOnEveryRead = true };
        await Drive_Async(phone, away, NOON.AddMinutes(7), NOON.AddHours(2).AddMinutes(10), isAway: true);

        Assert.Equal(
            [NOON.AddMinutes(30), NOON.AddMinutes(60), NOON.AddMinutes(90), NOON.AddMinutes(120)],
            [.. away.Posts.Select(post => post.At)]);

        // And what went out while away was the DIGEST, not the status on the digest's clock.
        Assert.All(away.Posts, post => Assert.StartsWith("🌙", post.Text, StringComparison.Ordinal));
        Assert.Equal(0, away.MemberStatusBuilds);
    }

    /// <summary>
    /// AND IT STILL DOES NOT WAKE A SESSION FOR NOTHING. A status entry is APPENDED TO THE CHANNEL,
    /// and an append is what a session's watcher fires on. Whatever D1 answers, an unchanged status
    /// must not be posted — the no-change guard is a correctness guard, not a politeness one.
    ///
    /// <para>
    /// The text the stub hands back differs on EVERY read ("last wrote N min ago", as the real roster
    /// does), while what the guard compares stays put. So one post and then silence proves the guard
    /// compares the stable reading — compared on the posted text it would post at every slot, which is
    /// the fork's 19:00/19:30/20:00. The second half proves it is a guard and not a stopped cadence.
    /// The roster changes once at 12:15 so the 12:30 boundary has something to say: first sight at
    /// 12:07 remembers the status it did not send (<see cref="AnAppRestart_DoesNotRepostAnUnchangedStatus"/>).
    /// </para>
    /// </summary>
    [Fact]
    public async Task AnUnchangedStatus_IsNotPosted()
    {
        var phone = Load_Phone();
        var host = new StubHost();

        await Drive_Async(phone, host, NOON.AddMinutes(7), NOON.AddMinutes(15), isAway: false);

        host.StableRoster = "orch: 1/3 done (33%) · 1 running\n- supervisor: idle — waiting\n- imp-1: report filed";

        await Drive_Async(phone, host, NOON.AddMinutes(15), NOON.AddHours(3).AddMinutes(10), isAway: false, sweep: host.Sweep);

        Assert.Equal([NOON.AddMinutes(30)], [.. host.Posts.Select(post => post.At)]);

        host.StableRoster = "orch: 2/3 done (66%)\n- supervisor: idle — waiting\n- imp-1: standing by";

        await Drive_Async(phone, host, NOON.AddHours(3).AddMinutes(10), NOON.AddHours(4).AddMinutes(10), isAway: false, sweep: host.Sweep);

        Assert.Equal([NOON.AddMinutes(30), NOON.AddHours(3).AddMinutes(30)], [.. host.Posts.Select(post => post.At)]);
    }

    /// <summary>
    /// A STATUS IS REMEMBERED ONLY ON A CONFIRMED WRITE — the away digest's rule, for the same reason.
    /// Remembering one whose append was dropped (a channel locked for the whole budget) would make the
    /// identical next one "unchanged", and the owner would never see it at all.
    /// </summary>
    [Fact]
    public async Task AStatusWhoseAppendFailed_IsSentAgainAtTheNextSlot()
    {
        var phone = Load_Phone();
        var host = new StubHost { PostSucceeds = false };

        await Drive_Async(phone, host, NOON.AddMinutes(7), NOON.AddMinutes(15), isAway: false);

        // Something to say at 12:30, and nothing more after it, so 13:00 re-sends THE SAME status.
        host.StableRoster = "orch: 2/3 done (66%)\n- supervisor: idle — waiting\n- imp-1: standing by";

        await Drive_Async(phone, host, NOON.AddMinutes(15), NOON.AddMinutes(40), isAway: false, sweep: host.Sweep);

        host.PostSucceeds = true;

        await Drive_Async(phone, host, NOON.AddMinutes(40), NOON.AddMinutes(100), isAway: false, sweep: host.Sweep);

        // 13:30 finds it written and unchanged, and stays silent.
        Assert.Equal([NOON.AddMinutes(30), NOON.AddMinutes(60)], [.. host.Posts.Select(post => post.At)]);
        Assert.Equal([false, true], [.. host.Posts.Select(post => post.Landed)]);
    }

    /// <summary>
    /// AN APP RESTART IS INVISIBLE ON THE PHONE (review of 8d548f0, 2026-09-23). The sweep's memories
    /// live in process, so every restart is first sight for every orchestration; with an empty key store
    /// the first boundary after it always read as changed, and each topic got one status repeating what
    /// the owner was last told. First sight now remembers the status it does not send, so the next
    /// boundary posts only if something moved, and the second half proves a change still gets through.
    /// </summary>
    [Fact]
    public async Task AnAppRestart_DoesNotRepostAnUnchangedStatus()
    {
        var phone = Load_Phone();
        var host = new StubHost();

        await Drive_Async(phone, host, NOON.AddMinutes(7), NOON.AddMinutes(15), isAway: false);
        host.StableRoster = "orch: 2/3 done (66%)\n- supervisor: idle — waiting\n- imp-1: standing by";
        await Drive_Async(phone, host, NOON.AddMinutes(15), NOON.AddMinutes(40), isAway: false, sweep: host.Sweep);

        Assert.Equal([NOON.AddMinutes(30)], [.. host.Posts.Select(post => post.At)]);

        // THE RESTART: a fresh sweep, nothing remembered, the same status on disk.
        await Drive_Async(phone, host, NOON.AddMinutes(40), NOON.AddMinutes(100), isAway: false);

        Assert.Equal([NOON.AddMinutes(30)], [.. host.Posts.Select(post => post.At)]);

        host.StableRoster = "orch: 3/3 done (100%)\n- supervisor: idle — waiting\n- imp-1: standing by";
        await Drive_Async(phone, host, NOON.AddMinutes(100), NOON.AddMinutes(130), isAway: false, sweep: host.Sweep);

        Assert.Equal([NOON.AddMinutes(30), NOON.AddMinutes(120)], [.. host.Posts.Select(post => post.At)]);
    }

    /// <summary>
    /// The away digest's version of the same rule — pinned by a source scan until the sweep left the
    /// engine (AwaySuppressesAppAlertsScanTests.TheAwayDigestIsRememberedOnlyAfterAConfirmedWrite).
    /// </summary>
    [Fact]
    public async Task AnAwayDigestWhoseAppendFailed_IsSentAgainAtTheNextSlot()
    {
        var phone = Load_Phone();
        var host = new StubHost { PostSucceeds = false };

        await Drive_Async(phone, host, NOON.AddMinutes(7), NOON.AddMinutes(40), isAway: true);

        host.PostSucceeds = true;

        await Drive_Async(phone, host, NOON.AddMinutes(40), NOON.AddMinutes(100), isAway: true, sweep: host.Sweep);

        // 13:00 re-sends the digest 12:30 failed to write; 13:30 finds it unchanged and stays silent.
        Assert.Equal([NOON.AddMinutes(30), NOON.AddMinutes(60)], [.. host.Posts.Select(post => post.At)]);
    }

    /// <summary>
    /// MASTER'S TRIGGER CAME BACK WITH ITS TEXT: nothing in flight — no <c>[&gt;]</c> line and no
    /// session that worked within the interval — posts nothing. The change guard would stop a repeat
    /// anyway; this stops the first "nothing is happening" of an orchestration nobody is working in.
    /// </summary>
    [Fact]
    public async Task WithNoWorkInFlight_NoStatusIsPosted()
    {
        var phone = Load_Phone();
        var host = new StubHost { StatusChangesOnEveryRead = true, Ledger = "# PLAN\n\n- [x] 1. done\n- [ ] 2. next\n", WorkedRecently = false };

        await Drive_Async(phone, host, NOON.AddMinutes(7), NOON.AddHours(2), isAway: false);

        Assert.Empty(host.Posts);

        host.WorkedRecently = true;

        await Drive_Async(phone, host, NOON.AddHours(2), NOON.AddHours(2).AddMinutes(40), isAway: false, sweep: host.Sweep);

        // A session that WORKED is work in flight with no `[>]` line at all — the 2026-08-20 silence.
        Assert.Equal([NOON.AddMinutes(120), NOON.AddMinutes(150)], [.. host.Posts.Select(post => post.At)]);
    }

    /// <summary>
    /// UNCHANGED BEHAVIOUR, PINNED: a paused orchestration (CLAUDE.md's PAUSE bullet names this sweep
    /// as a waker that must be gated) and one whose owner is in its terminal are skipped BEFORE the
    /// slot is stamped. Stamping during the skip would spend the boundary the owner is about to come
    /// back to — so the proof is a skip lifted inside the 13:00 boundary's grace: 13:00 must still go
    /// out, at 13:00:30. Stamped at 13:00:00 it would be Skip. Both cadences share the gate, so both
    /// are driven: the away rows are the behaviour that was already there, the status rows the new one.
    /// </summary>
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public async Task APausedOrMeetingOrchestration_IsSkippedBeforeTheSlotIsStamped(bool paused, bool inMeeting, bool isAway)
    {
        var phone = Load_Phone();
        var host = new StubHost { StatusChangesOnEveryRead = true, AwayDigestChangesOnEveryRead = true };
        var liftedAt = NOON.AddMinutes(60).AddSeconds(30);

        await Drive_Async(phone, host, NOON.AddMinutes(7), NOON.AddMinutes(40), isAway);
        Assert.Equal([NOON.AddMinutes(30)], [.. host.Posts.Select(post => post.At)]);

        var buildsBefore = host.MemberStatusBuilds + host.AwayDigestBuilds;

        await Drive_Async(phone, host, NOON.AddMinutes(40), liftedAt, isAway, sweep: host.Sweep, paused: paused, inMeeting: inMeeting);

        // Skipped outright: nothing built, nothing handed to the choke point.
        Assert.Single(host.Posts);
        Assert.Equal(buildsBefore, host.MemberStatusBuilds + host.AwayDigestBuilds);

        await Drive_Async(phone, host, liftedAt, NOON.AddMinutes(70), isAway, sweep: host.Sweep);

        Assert.Equal([NOON.AddMinutes(30), liftedAt], [.. host.Posts.Select(post => post.At)]);
    }

    // ---------------------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------------------

    IPhoneSettings Load_Phone()
    {
        return OrchestratorConfig_Loader.Load_OrEmpty(_paths).Phone;
    }

    /// <summary>
    /// The bridge's tick loop over [from, to), one sweep per 2-second tick with ONE clock reading each,
    /// as the engine calls it. A later call passing the same sweep continues the same memories.
    /// </summary>
    static async Task Drive_Async(
        IPhoneSettings phone,
        StubHost host,
        DateTime from,
        DateTime to,
        bool isAway,
        IPeriodicStatusSweep? sweep = null,
        bool paused = false,
        bool inMeeting = false)
    {
        host.Sweep = sweep ?? PeriodicStatusSweep_Factory.Create();

        var session = OrchestrationSession_Factory.Create(
            "orch-1", "repo", "/tmp/repo", new DateTime(2026, 9, 23, 8, 0, 0, DateTimeKind.Utc),
            42, null, null, null, "orch", null, null, [],
            TelegramDeliveryModes.Normal, null,
            ownerPresence: inMeeting ? OwnerPresenceModes.Terminal : OwnerPresenceModes.Remote,
            paused: paused);

        for (var now = from; now < to; now = now.AddSeconds(TICK_SECONDS))
        {
            host.Now = now;
            await host.Sweep.Push_Async(now, isAway, phone, [session], host, CancellationToken.None);
        }
    }

    /// <summary>
    /// The engine's side of the sweep, as a stub: records what is posted and when, and hands back a
    /// roster whose posted form moves on every read the way the real one does ("last wrote N min
    /// ago"), while its stable form moves only when a test says so.
    /// </summary>
    sealed class StubHost : IPeriodicStatusHost
    {
        public IPeriodicStatusSweep Sweep { get; set; } = PeriodicStatusSweep_Factory.Create();

        public DateTime Now { get; set; }

        public string StableRoster { get; set; } = "orch: 1/3 done (33%) · 1 running\n- supervisor: idle — waiting\n- imp-1: working now";

        public bool StatusChangesOnEveryRead { get; set; }

        public bool AwayDigestChangesOnEveryRead { get; set; }

        public string Ledger { get; set; } = LEDGER_WITH_WORK_IN_FLIGHT;

        public bool WorkedRecently { get; set; } = true;

        public bool PostSucceeds { get; set; } = true;

        public int MemberStatusBuilds { get; private set; }

        public int AwayDigestBuilds { get; private set; }

        public List<(DateTime At, string Text, bool Landed)> Posts { get; } = [];

        int _reads;

        public string Build_AwayDigest(IOrchestrationSession session)
        {
            AwayDigestBuilds++;

            return AwayDigestChangesOnEveryRead ? $"🌙 imp-1: working ({Now:HH:mm:ss})" : "🌙 imp-1: working";
        }

        public string Build_MemberStatus(IOrchestrationSession session, PlanProgressSnapshot? previous, bool withVolatileReadings)
        {
            MemberStatusBuilds++;
            _reads++;

            var stable = StatusChangesOnEveryRead ? $"{StableRoster} ({Now:HH:mm:ss})" : StableRoster;

            return withVolatileReadings ? $"{stable} · last wrote {_reads} min ago" : stable;
        }

        public IPlanProgress? Read_PlanProgress_OrNull(string orchId)
        {
            return PlanLedger_Parser.Parse_OrNull(Ledger);
        }

        public bool Has_AnySessionWorkedWithin(IOrchestrationSession session, int minutes)
        {
            return WorkedRecently;
        }

        public Task<string> Build_ScreenshotMarker_OrEmpty_Async(IOrchestrationSession session, CancellationToken cancellationToken)
        {
            return Task.FromResult(string.Empty);
        }

        public bool Post_StatusEntry(string orchId, string text, OwnerPresenceModes presence)
        {
            Posts.Add((Now, text, PostSucceeds));
            return PostSucceeds;
        }
    }
}
