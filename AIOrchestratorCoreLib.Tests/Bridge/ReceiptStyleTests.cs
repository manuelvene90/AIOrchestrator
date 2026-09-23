using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// <c>phone.receipts</c> DECIDES HOW THE OWNER'S MESSAGE IS ACKNOWLEDGED (plan 03 Task 6), driven
/// through the real engine under each shipped preset.
///
/// <para>
/// The owner, 2026-09-23: *"I like the double tick message to confirm that the message has arrived and
/// then that the message has been handed to the sup/solo, he prefers the stupid reactions."* Classic —
/// the owner's machine, and any machine that names no preset — is <c>ticks</c>: a silent ✓ under the
/// message, edited to ✓✓ when a session is handed it. Quiet — the fork author's — is <c>reactions</c>:
/// 👀 on the owner's own bubble, then 👌. Until this task the engine tried the reaction whatever the
/// setting said, so classic's ✓ existed only as the fallback for a refused reaction.
/// </para>
/// <para>
/// Every probe follows ONE receipt to delivery rather than stopping at the first mark, because the ✓
/// the owner asked for is two states, and a ✓ that is never edited is half of it.
/// </para>
/// </summary>
public class ReceiptStyleTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 6262;

    const string TICK = "✓";
    const string DOUBLE_TICK = "✓✓";
    const string DELIVERED = "Owner message delivered";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly RecordingLog_Fake _log = new();
    readonly ScriptedInbound_Fake _telegram = new();

    public ReceiptStyleTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-receipt-style-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
    }

    public void Dispose()
    {
        TempTree.Delete_BestEffort(_tempRoot);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// QUIET: 👀 on the owner's own message, and no ✓ line in the topic — then 👌 when a session has it,
    /// still with no line.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderQuiet_TheReceiptIsAReactionOnTheOwnersMessage_AndNoTickIsSent()
    {
        const long OWNER_MESSAGE_ID = 811;

        var engine = Build_Engine($"\"preset\":\"{Presets_Loader.QUIET}\"");

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Message_Json("look at this", 8101, OWNER_MESSAGE_ID)));

            Assert.True(
                await Wait_Until_Async(() => _telegram.Reactions.Contains((OWNER_MESSAGE_ID, OwnerReaction_Emoji.PICKED_UP)), 20_000),
                $"the owner's message never went 👀 → 👌.{Environment.NewLine}{Describe_Traffic()}");
        });

        Assert.Equal((OWNER_MESSAGE_ID, OwnerReaction_Emoji.RECEIVED), _telegram.Reactions[0]);
        Assert.Equal(0, _telegram.Count_Sent_Containing(TICK));
    }

    /// <summary>
    /// CLASSIC: THE DOUBLE TICK, END TO END. The ✓ is sent SILENT (they are holding the phone that sent
    /// the message a second ago), it becomes ✓✓ in place when the message is handed to the session, and
    /// NO reaction is ever attempted — neither the 👀 nor the 👌.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderClassic_NoReactionIsAttempted_AndASilentTickBecomesTheDoubleTick()
    {
        var engine = Build_Engine($"\"preset\":\"{Presets_Loader.CLASSIC}\"");

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Message_Json("look at this", 8201, 821)));

            Assert.True(
                await Wait_Until_Async(() => Find_Tick_OrNull() != null && _log.Has_Info_Containing(DELIVERED), 20_000),
                $"the ✓ was never sent, or the message was never delivered.{Environment.NewLine}{Describe_Traffic()}");

            var tick = Find_Tick_OrNull()!.Value;

            Assert.True(
                await Wait_Until_Async(() => _telegram.TextEdits.Contains((tick.Id, DOUBLE_TICK)), 10_000),
                $"the message was delivered and its ✓ never became ✓✓.{Environment.NewLine}{Describe_Traffic()}");

            Assert.Equal(TelegramSendSounds.Silent, tick.Sound);
        });

        Assert.Equal(0, _telegram.Reaction_Attempts);
    }

    /// <summary>
    /// THE FALLBACK IS NOT THE SETTING. Under quiet, a reaction Telegram refuses still leaves the owner
    /// a silent ✓ — and that ✓ becomes ✓✓ like any other, because the delivery edits what was actually
    /// sent rather than what the setting asked for.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderQuiet_ARefusedReaction_StillSendsTheSilentTick_WhichBecomesTheDoubleTick()
    {
        var engine = Build_Engine($"\"preset\":\"{Presets_Loader.QUIET}\"");

        _telegram.Refuse_Reactions("Bad Request: REACTION_INVALID");

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Message_Json("look at this", 8301, 831)));

            Assert.True(
                await Wait_Until_Async(() => Find_Tick_OrNull() != null && _log.Has_Info_Containing(DELIVERED), 20_000),
                "the reaction was refused and nothing took its place — an acknowledgement that silently did not "
                    + $"happen is the owner watching their message vanish.{Environment.NewLine}{Describe_Traffic()}");

            var tick = Find_Tick_OrNull()!.Value;

            Assert.True(
                await Wait_Until_Async(() => _telegram.TextEdits.Contains((tick.Id, DOUBLE_TICK)), 10_000),
                $"the fallback ✓ never became ✓✓.{Environment.NewLine}{Describe_Traffic()}");

            Assert.Equal(TelegramSendSounds.Silent, tick.Sound);
        });

        // THE REACTION WAS ASKED FOR, so this is the fallback path and not ticks under another name.
        Assert.True(_telegram.Reaction_Attempts >= 1, $"no reaction was attempted under quiet.{Environment.NewLine}{Describe_Traffic()}");
        Assert.Empty(_telegram.Reactions);
    }

    /// <summary>
    /// READ AT THE POINT OF EFFECT, NEVER CACHED. The first message is acknowledged with a reaction; the
    /// key is edited to ticks with the engine running; the very next message gets the ✓ and not one more
    /// reaction is asked for. A copy held in an engine field would react twice.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AnEditToTicks_IsObeyedByTheNextMessage_WithoutARestart()
    {
        const long FIRST_OWNER_MESSAGE_ID = 841;

        var engine = Build_Engine("\"phone\":{\"receipts\":\"reactions\"}");

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Message_Json("look at this", 8401, FIRST_OWNER_MESSAGE_ID)));

            // To the 👌, so the first exchange has asked for every reaction it ever will.
            Assert.True(
                await Wait_Until_Async(() => _telegram.Reactions.Contains((FIRST_OWNER_MESSAGE_ID, OwnerReaction_Emoji.PICKED_UP)), 20_000),
                $"the first message never went 👀 → 👌 under reactions.{Environment.NewLine}{Describe_Traffic()}");

            var attemptsBeforeTheEdit = _telegram.Reaction_Attempts;

            Write_Config("\"phone\":{\"receipts\":\"ticks\"}");

            _telegram.Queue_Updates(Updates_Json(Message_Json("and this too", 8402, 842)));

            Assert.True(
                await Wait_Until_Async(() => Find_Tick_OrNull() != null && Count_Deliveries() >= 2, 20_000),
                $"the message after the edit got no ✓, or was never delivered.{Environment.NewLine}{Describe_Traffic()}");

            var tick = Find_Tick_OrNull()!.Value;

            Assert.True(
                await Wait_Until_Async(() => _telegram.TextEdits.Contains((tick.Id, DOUBLE_TICK)), 10_000),
                $"the ✓ after the edit never became ✓✓.{Environment.NewLine}{Describe_Traffic()}");

            Assert.Equal(attemptsBeforeTheEdit, _telegram.Reaction_Attempts);
        });
    }

    // ---------------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------------

    /// <summary>The ✓ receipt — a send whose text is exactly the tick, not a line that merely contains one.</summary>
    (long Id, string Text, TelegramSendSounds Sound)? Find_Tick_OrNull()
    {
        var ticks = _telegram.Sent_WithIds.Where(sent => sent.Text == TICK).ToList();

        return ticks.Count == 0 ? null : ticks[^1];
    }

    int Count_Deliveries()
    {
        return _log.Dump().Split(Environment.NewLine).Count(line => line.Contains(DELIVERED, StringComparison.Ordinal));
    }

    string Describe_Traffic()
    {
        return "sent: " + string.Join(" | ", _telegram.Sent_WithIds.Select(sent => $"#{sent.Id} [{sent.Sound}] {sent.Text}"))
            + $"{Environment.NewLine}edits: " + string.Join(" | ", _telegram.TextEdits.Select(edit => $"#{edit.MessageId} {edit.Text}"))
            + $"{Environment.NewLine}reactions: " + string.Join(", ", _telegram.Reactions.Select(reaction => $"{reaction.MessageId}:{reaction.Emoji}"))
            + $" (attempts {_telegram.Reaction_Attempts}){Environment.NewLine}{_log.Dump()}";
    }

    /// <summary>
    /// A new provider per fact, with the keys written before it exists. EVERY ROLE IS PINNED TO THE
    /// TERMINAL RUNNER: quiet names bridge-driven runners, and a registered print session makes the
    /// engine's dispatcher reach for a LIVE <c>claude</c> (WhoRingsUnderEachPresetTests has the account).
    /// </summary>
    IBridgeEngine Build_Engine(string configJson)
    {
        Write_Config(configJson);

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);

        foreach (var role in SessionRole_Names.ALL)
            Assert.Equal(SessionRunners.Terminal, configProvider.Get_Current().Runners.Get_ForRole(role).Runner);

        var launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), _log);

        var session = launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        return BridgeEngine_Factory.Create_WithTelegramClient(_paths, configProvider, _store, launcher, _log, _telegram, BridgeTestTiming.Fast());
    }

    /// <summary>
    /// The stamp is moved forward on every write: the provider reloads on the file's write stamp, and two
    /// writes inside one timestamp tick would read as no edit at all.
    /// </summary>
    void Write_Config(string configJson)
    {
        var terminalRunners = string.Join(
            ",",
            SessionRole_Names.ALL.Select(role => $"\"{SessionRole_Names.Get_ConfigKey(role)}\":{{\"runner\":\"terminal\"}}"));

        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID},{configJson},\"runners\":{{{terminalRunners}}}}}");

        var previous = _lastConfigStampUtc;
        _lastConfigStampUtc = (previous == default ? DateTime.UtcNow : previous).AddSeconds(2);
        File.SetLastWriteTimeUtc(_paths.ConfigFile, _lastConfigStampUtc);
    }

    DateTime _lastConfigStampUtc;

    static string Updates_Json(string update) => "{\"ok\":true,\"result\":[" + update + "]}";

    static string Message_Json(string text, long updateId, long messageId)
    {
        return $"{{\"update_id\":{updateId},\"message\":{{\"message_id\":{messageId},"
            + $"\"message_thread_id\":{TOPIC_ID},\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"text\":\"{text}\"}}}}";
    }

    static async Task Run_WhileAsync(IBridgeEngine engine, Func<Task> body)
    {
        using var cancellation = new CancellationTokenSource();

        var loop = engine.Run_Async(cancellation.Token);

        try
        {
            await body();
        }
        finally
        {
            await cancellation.CancelAsync();

            try
            {
                await loop;
            }
            catch (OperationCanceledException)
            {
                // The only way these loops end.
            }
        }
    }

    static async Task<bool> Wait_Until_Async(Func<bool> condition, int maxMilliseconds)
    {
        for (var waited = 0; waited < maxMilliseconds; waited += 100)
        {
            if (condition())
                return true;

            await Task.Delay(100);
        }

        return condition();
    }
}
