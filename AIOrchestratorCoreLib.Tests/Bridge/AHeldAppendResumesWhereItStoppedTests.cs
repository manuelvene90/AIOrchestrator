using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.Status;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// A HELD APPEND RESUMES WHERE IT STOPPED — COUNTING THE ENTRIES IT SUPPRESSED.
///
/// <para>
/// The hold is per ENTRY, so an append can be delivered in part: `_deliveredEntriesOfHeldAppend`
/// remembers how many of its entries were already dealt with, and the next poll — which re-emits the
/// whole unconfirmed append from the front — skips that many. The skip is POSITIONAL: it counts off
/// the first N entries of the batch without looking at them.
/// </para>
/// <para>
/// THE MEMO THEREFORE HAS TO COUNT ENTRIES CONSUMED, NOT ENTRIES SENT, and it counted sent. An entry
/// the push policy refuses took the `continue` above the counter, so every suppressed entry ahead of
/// a sent one left the prefix one short — and the resume, counting positionally, stopped one entry
/// early and RE-SENT something the owner already had. Bounded (one duplicate, never a loss) and
/// therefore quiet, which is exactly why it needs a test rather than a report.
/// </para>
/// <para>
/// The shape is production's own, with no timing games: the suppressed entry is an owner restatement
/// (the one thing this build still refuses), the entry after it is a QUESTION — whose SEND is what
/// raises the awaiting-answer flag — and the entry after THAT meets the hold. A fourth entry is
/// written only so the tailer's trailing-entry rule withholds it instead of the third, keeping the
/// first three inside ONE append, which is the whole premise.
/// </para>
/// <para>
/// TWO MODES (plan 03, <c>phone.push</c>). The fixture selects <c>everything</c>, the mode this file was
/// written under, where a restatement is the only entry not sent. Under <c>filtered</c> the same skip is
/// reached a second way — by narration HELD for the turn-end digest — and that entry is consumed, so
/// counted, exactly as a dropped one is; the second fact pins it.
/// </para>
/// </summary>
public class AHeldAppendResumesWhereItStoppedTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445599;
    const long OWNER_USER_ID = 555000114;
    const long TOPIC_ID = 4545;

    /// <summary>Carried ONLY in the `QUESTION:` line, so it lands on the button message and nowhere else.</summary>
    const string QUESTION_SENTINEL = "DELTAKEY";

    const string HELD_TEXT = "The worktree is still open and the branch is not pushed yet.";

    /// <summary>The first entry of the everything fact: the one thing that mode refuses.</summary>
    const string RESTATEMENT_ENTRY = "reading you back\nOwner: \"how should futures roll by default\"";

    /// <summary>The first entry of the filtered fact: plain narration, which that mode holds for the digest.</summary>
    const string NARRATION_ENTRY = "progress\nimp-1 is pricing the matrix; rev-1 has the diff.";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;

    // NOT READONLY for one reason: Rebuild_Engine_UnderFiltered replaces both before the engine has
    // ever run. Nothing else assigns them.
    IOrchestrationLauncher _launcher;
    IBridgeEngine _engine;
    readonly FailableTelegram_Fake _telegram;
    readonly RecordingLog_Fake _log;

    public AHeldAppendResumesWhereItStoppedTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-held-resume-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        // phone.push = everything, the mode this file was written under (see the class summary).
        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID},\"phone\":{{\"push\":\"everything\"}}}}");

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _log = new RecordingLog_Fake();
        _telegram = new FailableTelegram_Fake();

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), _log);
        _engine = BridgeEngine_Factory.Create_WithTelegramClient(_paths, configProvider, _store, _launcher, _log, _telegram, BridgeTestTiming.Fast());
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AnEntrySuppressedAheadOfASentOne_IsCountedBySkipOrTheQuestionIsTextedTwice()
    {
        var orchId = await Start_WithChannelAlreadySeen_Async();

        Append_Entries(orchId, RESTATEMENT_ENTRY);

        Assert.True(
            await Run_Until_Async(() => AwaitingAnswerFlag_Marker.Is_Raised(_paths, orchId), 20_000),
            $"the question was never texted, so nothing was ever held and this test reached nothing.{Environment.NewLine}{_log.Dump()}");

        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("held entry #3"), 20_000),
            $"the third entry was not held behind the question — the premise of this test is gone.{Environment.NewLine}{_log.Dump()}");

        // The owner answers, in effect: the hold lifts and the unconfirmed append is re-emitted from
        // the front. This is the moment the positional skip is read.
        AwaitingAnswerFlag_Marker.Clear(_paths, orchId, out _);

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_Sent_Containing(HELD_TEXT), 20_000),
            $"the held entry never came out after the hold lifted.{Environment.NewLine}{_log.Dump()}");

        Assert.Equal(1, _telegram.Count_Attempts_Containing(QUESTION_SENTINEL));
    }

    /// <summary>
    /// MEASURES <c>phone.push = filtered</c>. The same positional skip, reached by an entry the filter
    /// HOLDS for the turn-end digest rather than one it drops: held is consumed, so held is counted. If
    /// it were not, the prefix would be one short and the resume would text the question a second time.
    ///
    /// <para>
    /// HELD_TEXT cannot be the probe here — it is narration, so this mode holds it too and it never
    /// reaches the phone. The probe is the resume PASS itself: the engine is stopped between runs, so
    /// the log is counted exactly at the lift, and a pass logged after that is the re-emission reading
    /// the skip. After it, the question must have been attempted once and entry #3 never held again.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderFiltered_AnEntryHeldForTheDigestAheadOfASentOne_IsCountedBySkipOrTheQuestionIsTextedTwice()
    {
        Rebuild_Engine_UnderFiltered();

        var orchId = await Start_WithChannelAlreadySeen_Async();

        Append_Entries(orchId, NARRATION_ENTRY);

        Assert.True(
            await Run_Until_Async(() => AwaitingAnswerFlag_Marker.Is_Raised(_paths, orchId), 20_000),
            $"the question was never texted, so nothing was ever held and this test reached nothing.{Environment.NewLine}{_log.Dump()}");

        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("held entry #3"), 20_000),
            $"the third entry was not held behind the question — the premise of this test is gone.{Environment.NewLine}{_log.Dump()}");

        AwaitingAnswerFlag_Marker.Clear(_paths, orchId, out _);

        var passesAtTheLift = Count_LogLines_Containing("entry #3 FROM Supervisor");
        var holdsAtTheLift = Count_LogLines_Containing("held entry #3");

        Assert.True(
            await Run_Until_Async(() => Count_LogLines_Containing("entry #3 FROM Supervisor") > passesAtTheLift, 20_000),
            $"the held append was never re-emitted after the hold lifted, so the skip was never read.{Environment.NewLine}{_log.Dump()}");

        // A few more ticks, so a second question send that was merely in flight has had time to land.
        await Run_For_Async(BridgeTestTiming.Window_ForTicks(5));

        Assert.Equal(1, _telegram.Count_Attempts_Containing(QUESTION_SENTINEL));
        Assert.Equal(holdsAtTheLift, Count_LogLines_Containing("held entry #3"));
    }

    int Count_LogLines_Containing(string fragment)
    {
        return _log.Dump()
            .Split(Environment.NewLine)
            .Count(line => line.Contains(fragment, StringComparison.Ordinal));
    }

    /// <summary>
    /// SELECTS <c>phone.push = filtered</c> by writing the classic fixture — no <c>phone</c> block, no
    /// preset — and rebuilds on a NEW provider before the engine has ever run: the provider caches on the
    /// write stamp, and a rewrite inside the same stamp would leave this measuring everything.
    /// </summary>
    void Rebuild_Engine_UnderFiltered()
    {
        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID}}}");

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);

        Assert.Equal(PhonePushModes.Filtered, configProvider.Get_Current().Phone.Push);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), _log);
        _engine = BridgeEngine_Factory.Create_WithTelegramClient(_paths, configProvider, _store, _launcher, _log, _telegram, BridgeTestTiming.Fast());
    }

    /// <summary>
    /// Four entries in one write. The first is <paramref name="firstEntry"/> (subject line, then body) —
    /// an entry the mode under test does NOT send. The second asks, which is what raises the flag. The
    /// third meets the hold. The fourth exists so the tailer withholds IT as the trailing entry and the
    /// first three travel together.
    /// </summary>
    void Append_Entries(string orchId, string firstEntry)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        var text =
            $"\n## [1] FROM supervisor — {stamp} — {firstEntry}\n"
            + $"\n## [2] FROM supervisor — {stamp} — a decision\n"
            + "RECOMMEND: Keep — it is the branch the ledger already names.\nRISK: low\nROW: none\n"
            + $"QUESTION: Which branch carries the {QUESTION_SENTINEL} change?\nOPTION: Keep\nOPTION: Replace\n"
            + $"\n## [3] FROM supervisor — {stamp} — where the work stands\n{HELD_TEXT}\n"
            + $"\n## [4] FROM supervisor — {stamp} — trailing\nHeld back by the tailer's quiet rule.\n";

        File.AppendAllText(channelFile, text);
    }

    void Seed_OwnerChannel(string orchId)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");
    }

    /// <summary>An unseen file is registered at its current end and emits nothing, so the channel is baselined first.</summary>
    async Task<string> Start_WithChannelAlreadySeen_Async()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);
        Seed_OwnerChannel(session.OrchId);

        await Run_For_Async(BridgeTestTiming.Window_ForTicks(3));

        return session.OrchId;
    }

    async Task Run_For_Async(int milliseconds)
    {
        await Run_Until_Async(() => false, milliseconds);
    }

    async Task<bool> Run_Until_Async(Func<bool> condition, int maxMilliseconds)
    {
        using var cancellation = new CancellationTokenSource();

        var loop = _engine.Run_Async(cancellation.Token);
        var satisfied = false;

        for (var waited = 0; waited < maxMilliseconds; waited += 100)
        {
            if (condition())
            {
                satisfied = true;
                break;
            }

            await Task.Delay(100);
        }

        await cancellation.CancelAsync();

        try
        {
            await loop;
        }
        catch (OperationCanceledException)
        {
            // The only way these loops end.
        }

        return satisfied || condition();
    }
}
