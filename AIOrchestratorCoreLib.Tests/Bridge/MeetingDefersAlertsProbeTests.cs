using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// A meeting must DEFER the app's attention traffic, never destroy it — driven through the REAL
/// engine, because the thing that goes wrong is engine state and no pure unit can be asked about it.
///
/// <para>
/// This harness is the answer to a claim I made twice in commit messages and which was wrong:
/// "nothing in the suite constructs BridgeEngineModel, so a test would have to start the loop, sleep
/// past a tick and assert through the filesystem". <c>BridgeEngine_Factory.Create</c> is PUBLIC —
/// the type being internal blocks NAMING it, not building it — and starting the loop with a token
/// cancelled immediately runs exactly one tick with no sleep, because the tick body runs to
/// completion before the loop's delay observes the cancellation. Two probe classes were already
/// doing this at the time I wrote that sentence (rev-7 P3, 2026-08-13).
/// </para>
/// <para>
/// WHICH CASES ACTUALLY PIN WHAT — measured by rev-6 running this file verbatim on the pre-fix tree
/// rather than reasoning about it. <c>ASpellThatENDEDDuringAMeeting_DoesNotSilenceTheNextOne</c> and
/// <c>AnAppendThatTHROWS_DoesNotTakeTheRestOfTheTickWithIt</c> FAIL without their fix. The two named
/// <c>Invariant…</c> PASS pre-fix and cannot discriminate, because
/// <c>Append_SupervisorAttention_UnlessMeeting</c> carries its own meeting check at the append, which
/// satisfies them whichever side of the release the bail sits on. They are kept because they are
/// true and something must hold them, and renamed because three guard-sounding names for one guard
/// is how the next reader concludes a fix is covered when it is not — the same discipline
/// <c>NudgeOncePerThingProbeTests</c> documents, where two cases the control showed to be vacuous
/// were deleted outright.
/// </para>
/// </summary>
public class MeetingDefersAlertsProbeTests : IDisposable
{
    const string NudgeSubject = "unread reports waiting on you";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;

    public MeetingDefersAlertsProbeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-meeting-defer-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        _store = OrchestrationSessionStore_Factory.Create(_paths);

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var log = OrchestrationLog_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), log);
        _engine = BridgeEngine_Factory.Create(_paths, configProvider, _store, _launcher, log);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// THE STUCK TOKEN. The nudge fires once per quiet spell and remembers that it did. Entering a
    /// meeting used to bail ABOVE the line that RELEASES that memory, so a spell which genuinely
    /// ended during the meeting was never cleared — and the next spell, with a real unanswered
    /// report in it, was silently un-nudgeable.
    /// <para>
    /// It asserts the NUDGE, not where any guard sits, so it reddens for any placement that skips
    /// the release.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ASpellThatENDEDDuringAMeeting_DoesNotSilenceTheNextOne()
    {
        var (orchId, memberChannel) = Start_WithDormantMember();
        var ownerChannel = _paths.Get_OwnerChannelFile(orchId);

        await Tick_Once_Async();
        Assert.True(Wait_Until(() => Count_Nudges(ownerChannel) == 1), "the supervisor was never nudged for the first spell");

        // The owner walks over. Directed work continues in a meeting, so the supervisor answers the
        // outstanding report while they are talking — the spell is genuinely over.
        _store.Set_OwnerPresence(orchId, OwnerPresenceModes.Terminal);
        Answer_TheReport(memberChannel);

        await Tick_Once_Async();
        Assert.Equal(1, Count_Nudges(ownerChannel));

        // The owner leaves, and a member files something new: a NEW spell, with a real unanswered
        // report in it.
        _store.Set_OwnerPresence(orchId, OwnerPresenceModes.Remote);
        File_AFreshReport(memberChannel);

        await Tick_Once_Async();

        Assert.True(
            Wait_Until(() => Count_Nudges(ownerChannel) == 2),
            "the new spell was never nudged — the token from the spell that ended during the meeting was still held");
    }

    /// <summary>
    /// INVARIANT, NOT A GUARD FOR THE BAIL — and this is measured rather than argued. rev-6 ran the
    /// control: with this file copied verbatim onto the pre-fix tree, this case PASSES. It cannot
    /// discriminate, because <c>Append_SupervisorAttention_UnlessMeeting</c> carries its own meeting
    /// check at the append, which satisfies this whichever side of the release the bail sits on.
    /// <para>
    /// It is kept because it is true and worth holding — a fix of "nudge regardless" would redden
    /// it — but it is named for what it pins: the CHOKE POINT suppresses, not the bail's placement.
    /// Only <c>ASpellThatENDEDDuringAMeeting_DoesNotSilenceTheNextOne</c> fails pre-fix.
    /// </para>
    /// </summary>
    [Fact]
    public async Task InvariantDuringAMeeting_TheChokePointSuppressesTheNudge()
    {
        var (orchId, _) = Start_WithDormantMember();
        var ownerChannel = _paths.Get_OwnerChannelFile(orchId);

        _store.Set_OwnerPresence(orchId, OwnerPresenceModes.Terminal);

        await Tick_Once_Async();

        Assert.Equal(0, Count_Nudges(ownerChannel));
    }

    /// <summary>
    /// INVARIANT, same measurement: this also passes on the pre-fix tree, for the same reason — the
    /// choke point's own check lifts when presence returns, wherever the bail sits. Kept because
    /// "suppressed while they are there, delivered once they leave" is the owner's rule end to end
    /// and something must hold it; named so nobody reads it as covering the release.
    /// </summary>
    [Fact]
    public async Task InvariantAfterTheMeeting_TheChokePointDeliversTheHeldNudge()
    {
        var (orchId, _) = Start_WithDormantMember();
        var ownerChannel = _paths.Get_OwnerChannelFile(orchId);

        _store.Set_OwnerPresence(orchId, OwnerPresenceModes.Terminal);
        await Tick_Once_Async();
        Assert.Equal(0, Count_Nudges(ownerChannel));

        _store.Set_OwnerPresence(orchId, OwnerPresenceModes.Remote);
        await Tick_Once_Async();

        Assert.True(Wait_Until(() => Count_Nudges(ownerChannel) == 1), "the nudge held during the meeting never arrived");
    }

    /// <summary>
    /// ONE BAD APPEND MUST NOT COST THE WHOLE TICK. ChannelAppender throws on failure, and these
    /// calls sit on the mirror tick with nothing around them — so a single throw escaped to the
    /// loop's catch, was logged as the generic "Mirror tick failed", and skipped everything after
    /// it: the poll, the mirror, the ledger check, compaction, the state persist.
    /// <para>
    /// The trigger is ordinary concurrency rather than a disaster: <c>File.AppendAllText</c> opens
    /// the target deny-write, so a second appender throws rather than interleaving, and this app
    /// runs two loops that both append. The exclusive lock below is that collision, made
    /// deterministic.
    /// </para>
    /// <para>
    /// It asserts the tick's LAST statement (<c>Persist_BridgeState</c>, which writes
    /// <c>.bridge-state.json</c>), because that is the thing that only exists if the tick ran all
    /// the way through — an assertion about the append itself would pass just as well on a tick that
    /// died immediately afterwards.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AnAppendThatTHROWS_DoesNotTakeTheRestOfTheTickWithIt()
    {
        var (orchId, _) = Start_WithDormantMember();
        var ownerChannel = _paths.Get_OwnerChannelFile(orchId);

        // The nudge is about to append here, and it cannot: another writer holds it deny-write.
        using var collision = File.Open(ownerChannel, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);

        await Tick_Once_Async();

        Assert.True(
            Wait_Until(() => File.Exists(_paths.BridgeStateFile)),
            "the tick died on the failed append — nothing after it ran, including the state persist");
    }

    /// <summary>
    /// A DEFERRED CHANNEL MUST NOT BE COMPACTED. Deferred means held and replayed; the owner is given
    /// that promise in writing. But a deferred channel is never POLLED, so the tailer holds no state
    /// for it and <c>Has_UnconfirmedEntries</c> answers false — "owes nothing" is indistinguishable
    /// from "never looked at" — after which compaction rewrote the file and moved the cursor to the
    /// END of it. Un-deferring then delivered nothing at all, permanently.
    /// <para>
    /// This asserts the FILE, not the guard: the live channel must still hold every entry and no
    /// <c>.archive.md</c> may appear beside it. Any future route to compacting a frozen channel
    /// reddens it, wherever the check is placed.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ADeferredChannelOverTheCompactionThreshold_IsNotCompacted()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var ownerChannel = _paths.Get_OwnerChannelFile(session.OrchId);

        // Above Channel_Compactor.COMPACT_ABOVE_ENTRIES (90), so a compaction really is on offer —
        // a fixture below the threshold would pass for the wrong reason.
        var entries = new System.Text.StringBuilder();

        for (var index = 1; index <= 95; index++)
            entries.Append($"## [{index}] FROM supervisor — 2026-08-13 10:00 — entry {index}\nbody {index}\n\n");

        File.WriteAllText(ownerChannel, entries.ToString());

        var lengthBefore = new FileInfo(ownerChannel).Length;

        _store.Set_TelegramMode(session.OrchId, TelegramDeliveryModes.Deferred);

        await Tick_Once_Async();

        Assert.Equal(lengthBefore, new FileInfo(ownerChannel).Length);
        Assert.False(File.Exists(Path.ChangeExtension(ownerChannel, null) + ".archive.md"), "the frozen channel was compacted — its held backlog is gone");
    }

    /// <summary>
    /// A FAILED APPEND MUST NOT SPEND THE TOKEN. The nudge fires once per quiet spell and remembers
    /// that it did; its only release is the spell ending. While a failed append could only THROW,
    /// taking the token with the tick, recording it first was harmless — the safe wrapper changed
    /// that by catching and returning false, so the token began saying "nudged" for an entry that was
    /// never written, and the supervisor was never told for the rest of that spell.
    /// <para>
    /// The lock is the same production collision as the tick test: deny-write, so the append really
    /// fails rather than being simulated.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ANudgeWhoseAppendFAILED_IsDeliveredOnTheNextTick()
    {
        var (orchId, _) = Start_WithDormantMember();
        var ownerChannel = _paths.Get_OwnerChannelFile(orchId);

        using (var collision = File.Open(ownerChannel, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
        {
            await Tick_Once_Async();
        }

        Assert.Equal(0, Count_Nudges(ownerChannel));

        // The obstruction is gone; the nudge is still owed and must arrive.
        await Tick_Once_Async();

        Assert.True(
            Wait_Until(() => Count_Nudges(ownerChannel) == 1),
            "the nudge was never delivered — its token was spent on the append that failed");
    }

    /// <summary>A briefed member that went silent twenty minutes ago, with a report nobody answered.</summary>
    (string OrchId, string MemberChannel) Start_WithDormantMember()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var memberChannel = _paths.Get_ImplementerChannelFile(session.OrchId, session.Members[0].MemberId);
        var stamp = DateTime.Now.AddMinutes(-20).ToString("yyyy-MM-dd HH:mm");

        File.WriteAllText(
            memberChannel,
            $"## [1] FROM supervisor — {stamp} — brief\nimplement the parser\n\n"
            + $"## [2] FROM implementer — {stamp} — TASK 1 done\nfiled, awaiting your verdict\n");

        File.SetLastWriteTime(memberChannel, DateTime.Now.AddMinutes(-20));

        return (session.OrchId, memberChannel);
    }

    static void Answer_TheReport(string memberChannel)
    {
        var stamp = DateTime.Now.AddMinutes(-20).ToString("yyyy-MM-dd HH:mm");

        File.AppendAllText(memberChannel, $"\n## [3] FROM supervisor — {stamp} — accepted\nverdict given\n");
        File.SetLastWriteTime(memberChannel, DateTime.Now.AddMinutes(-20));
    }

    static void File_AFreshReport(string memberChannel)
    {
        var stamp = DateTime.Now.AddMinutes(-20).ToString("yyyy-MM-dd HH:mm");

        File.AppendAllText(memberChannel, $"\n## [4] FROM implementer — {stamp} — TASK 2 done\nfiled, awaiting your verdict\n");
        File.SetLastWriteTime(memberChannel, DateTime.Now.AddMinutes(-20));
    }

    int Count_Nudges(string ownerChannel)
    {
        if (!File.Exists(ownerChannel))
            return 0;

        return ChannelEntry_Parser
            .Parse_All(File.ReadAllText(ownerChannel))
            .Count(entry => entry.Author == ChannelAuthors.App && entry.Subject.Contains(NudgeSubject));
    }

    async Task Tick_Once_Async()
    {
        using var cancellation = new CancellationTokenSource();

        var loop = _engine.Run_Async(cancellation.Token);

        await cancellation.CancelAsync();

        try
        {
            await loop;
        }
        catch (OperationCanceledException)
        {
            // The only way this loop ends.
        }
    }

    static bool Wait_Until(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (condition())
                return true;

            Thread.Sleep(50);
        }

        return false;
    }
}
