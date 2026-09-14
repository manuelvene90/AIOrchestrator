using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.Status;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// A HELD APPEND'S PREFIX MEMO DIES WITH THE APPEND IT COUNTS.
///
/// <para>
/// `_deliveredEntriesOfHeldAppend` is a POSITIONAL skip — "ignore the first N mirrorable entries of
/// this file's re-emitted append, without looking at them" — so it is only meaningful while the
/// cursor has NOT advanced. Every outcome except HELD lets the caller settle the append, which
/// confirms it and moves the cursor past those entries for ever. A memo that outlives that counts
/// off the front of an UNRELATED later batch, and the skip branch logs nothing at all: the owner
/// simply never receives what the supervisor wrote.
/// </para>
/// <para>
/// THE SEQUENCE IS ORDINARY, NOT A CORNER. The supervisor asks a question, the hold parks the rest
/// of the append with a prefix of 1; the owner walks to the PC and presence SILENCES the topic; the
/// silence gate returns Delivered above the entry loop, so the held append is confirmed and its
/// remainder dropped (silenced traffic is deliberately never replayed) — with the memo intact. The
/// owner leaves the terminal, and the next thing the supervisor writes to them loses its first
/// entry. Both of the two early Delivered returns above the entry loop (nothing mirrorable, silenced
/// topic) had this shape; the memo is now cleared on every non-Held exit, in one place.
/// </para>
/// <para>
/// The harness is <see cref="AHeldAppendResumesWhereItStoppedTests"/>'s: a real question raises the
/// real flag by being SENT, and the trailing entry exists only so the tailer's quiet rule withholds
/// IT and the entries before it travel in one append.
/// </para>
/// </summary>
public class AHeldAppendsPrefixDiesWithItsAppendTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445588;
    const long OWNER_USER_ID = 555000117;
    const long TOPIC_ID = 4747;

    /// <summary>Carried ONLY in the `QUESTION:` line, so it identifies the question message alone.</summary>
    const string QUESTION_SENTINEL = "OMEGAKEY";

    /// <summary>Held behind the question, then dropped by the silence — it must never reach the phone.</summary>
    const string DROPPED_TEXT = "This one is held behind the question and then silenced away.";

    /// <summary>The next thing the supervisor writes once the topic is audible again. THE ASSERTION.</summary>
    const string AFTERWARDS_TEXT = "The merge is done and the branch is pushed.";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;
    readonly FailableTelegram_Fake _telegram;
    readonly RecordingLog_Fake _log;

    public AHeldAppendsPrefixDiesWithItsAppendTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-held-prefix-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID}}}");

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
    public async Task AnAppendConfirmedWhileSilenced_DoesNotEatTheFrontOfTheNextOne()
    {
        var orchId = await Start_WithChannelAlreadySeen_Async();

        Append_QuestionThenHeldEntry(orchId);

        Assert.True(
            await Run_Until_Async(() => AwaitingAnswerFlag_Marker.Is_Raised(_paths, orchId), 20_000),
            $"the question was never texted, so nothing was ever held and this test reached nothing.{Environment.NewLine}{_log.Dump()}");

        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("held entry #2"), 20_000),
            $"the second entry was not held behind the question — the premise of this test is gone.{Environment.NewLine}{_log.Dump()}");

        // THE OWNER WALKS TO THE PC. The topic goes silent, the held append is confirmed on the next
        // poll and its remainder is dropped — deliberately, silenced traffic is never replayed.
        _store.Set_TelegramMode(orchId, TelegramDeliveryModes.Silenced);

        await Run_For_Async(BridgeTestTiming.Window_ForTicks(6));

        // They answer at the terminal and walk away again.
        AwaitingAnswerFlag_Marker.Clear(_paths, orchId, out _);
        _store.Set_TelegramMode(orchId, TelegramDeliveryModes.Normal);

        Append_Afterwards(orchId);

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_Sent_Containing(AFTERWARDS_TEXT), 20_000),
            "the entry written after the silence lifted never reached the phone — a stale positional "
            + $"prefix from the held append counted it off in silence.{Environment.NewLine}{_log.Dump()}");

        // THE PREMISE, ASSERTED LAST so its failure reads as "this test proved nothing" rather than
        // as the defect: if the held append had NOT been confirmed while silenced, it would have been
        // re-emitted on unsilencing, this text would be on the phone, and the memo would have been
        // cleared by that ordinary delivery instead of by the fix.
        Assert.False(
            _telegram.Has_Sent_Containing(DROPPED_TEXT),
            $"the held entry survived the silence, so the append was never confirmed under it.{Environment.NewLine}{_log.Dump()}");
    }

    /// <summary>
    /// Three entries in one write: [1] asks (its SEND is what raises the flag and it is the one
    /// entry the prefix will count), [2] meets the hold, [3] exists so the tailer withholds IT as
    /// the trailing entry and the first two travel together.
    /// </summary>
    void Append_QuestionThenHeldEntry(string orchId)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        var text =
            $"\n## [1] FROM supervisor — {stamp} — a decision\n"
            + "RECOMMEND: Keep — it is the branch the ledger already names.\nRISK: low\nROW: none\n"
            + $"QUESTION: Which branch carries the {QUESTION_SENTINEL} change?\nOPTION: Keep\nOPTION: Replace\n"
            + $"\n## [2] FROM supervisor — {stamp} — where the work stands\n{DROPPED_TEXT}\n"
            + $"\n## [3] FROM supervisor — {stamp} — trailing\nHeld back by the tailer's quiet rule.\n";

        File.AppendAllText(channelFile, text);
    }

    /// <summary>The batch whose front the stale prefix would eat, plus its own trailing entry.</summary>
    void Append_Afterwards(string orchId)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        var text =
            $"\n## [4] FROM supervisor — {stamp} — done\n{AFTERWARDS_TEXT}\n"
            + $"\n## [5] FROM supervisor — {stamp} — trailing\nHeld back by the tailer's quiet rule.\n";

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
