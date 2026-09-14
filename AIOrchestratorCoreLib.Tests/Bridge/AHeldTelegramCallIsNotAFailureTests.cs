using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Logging.OrchestrationLogEntry;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// A HELD CALL IS "NOT NOW", NOT A FAILURE — and the two surfaces that redraw every tick logged it as
/// one. <see cref="TelegramHeldException"/> exists precisely so a caller can tell a shut door from a
/// refusal ("a 429 storm would simply become a log storm"), yet no caller caught it: the PULSE status
/// line and the General dashboard both fell into their generic catch and wrote a WARNING.
///
/// Measured on the owner's panel, 2026-09-14 14:50-14:52: "Topic status line could not be updated —
/// Telegram call to 'msg:12759' was NOT attempted: rate-limit window held until …", one per open
/// orchestration, every ~40 s, plus "General dashboard not updated (… NOT attempted …)". Nothing
/// failed and nothing needed doing: the next tick after the window redraws the line.
///
/// Driven through the engine's real loop, with only the Telegram client faked: the button-row calls
/// both surfaces use throw what the fixture says, everything else is recorded normally.
/// </summary>
public class AHeldTelegramCallIsNotAFailureTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 5151;

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;

    public AHeldTelegramCallIsNotAFailureTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-held-call-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID}}}");

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");
    }

    public void Dispose()
    {
        TempTree.Delete_BestEffort(_tempRoot);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AHeldStatusLineOrDashboard_LogsNoWarning()
    {
        var (telegram, log) = await Run_WithButtonRowCallsThrowing_Async(
            () => new TelegramHeldException("msg:12759", DateTime.UtcNow.AddSeconds(30)));

        // The path was exercised — otherwise "no warning" would pass on a tick that never drew anything.
        Assert.True(telegram.ButtonRowCallsRefused > 0, "the fixture never reached a status line or dashboard draw");

        Assert.DoesNotContain(log.Entries, entry => entry.Level == LogLevels.Warning && entry.Message.Contains("NOT attempted"));
    }

    /// <summary>
    /// THE CONTROL: the same fixture with a REAL failure still warns. Without it the test above would
    /// pass just as well if the log were never read or the surfaces never logged anything.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ARealFailure_StillWarns()
    {
        var (_, log) = await Run_WithButtonRowCallsThrowing_Async(() => new Exception("Telegram 'editMessageText' failed with HTTP 500"));

        Assert.Contains(log.Entries, entry => entry.Level == LogLevels.Warning && entry.Message.Contains("HTTP 500"));
    }

    async Task<(ButtonRowsThrowingTelegram_Fake Telegram, RecordingLog Log)> Run_WithButtonRowCallsThrowing_Async(Func<Exception> failure)
    {
        var store = OrchestrationSessionStore_Factory.Create(_paths);
        var telegram = new ButtonRowsThrowingTelegram_Fake(failure);
        var log = new RecordingLog();
        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, store, new RecordingSpawner_Fake(), log);
        var engine = BridgeEngine_Factory.Create_WithTelegramClient(_paths, configProvider, store, launcher, log, telegram, BridgeTestTiming.Fast());

        var session = launcher.Start_Orchestration("Repo", _tempRepo);
        store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        using var cancellation = new CancellationTokenSource();
        var loop = engine.Run_Async(cancellation.Token);

        await Task.Delay(BridgeTestTiming.Window_ForTicks(4));
        await cancellation.CancelAsync();

        try
        {
            await loop;
        }
        catch (OperationCanceledException)
        {
            // The only way the loop ends.
        }

        return (telegram, log);
    }

    sealed class RecordingLog : IOrchestrationLog
    {
        readonly object _lock = new();
        readonly List<(LogLevels Level, string Message)> _entries = [];

        public IReadOnlyList<(LogLevels Level, string Message)> Entries
        {
            get
            {
                lock (_lock)
                    return [.. _entries];
            }
        }

        public void Log_Info(string orchId, string message) => Add(LogLevels.Info, message);
        public void Log_Warning(string orchId, string message) => Add(LogLevels.Warning, message);
        public void Log_Error(string orchId, string message, Exception? exception) => Add(LogLevels.Error, message);

        public event Action<IOrchestrationLogEntry>? EntryLogged { add { } remove { } }

        void Add(LogLevels level, string message)
        {
            lock (_lock)
                _entries.Add((level, message));
        }
    }

    /// <summary>Both redrawn surfaces go through the button-row calls; those throw, the rest is the recording fake.</summary>
    sealed class ButtonRowsThrowingTelegram_Fake(Func<Exception> failure) : ITelegramApiClient
    {
        readonly SurfaceRecordingTelegram_Fake _inner = new();
        int _refused;

        public int ButtonRowCallsRefused => Volatile.Read(ref _refused);

        Exception Refuse()
        {
            Interlocked.Increment(ref _refused);
            return failure();
        }

        public Task<long?> Send_MessageWithButtonRows_Async(long? messageThreadId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, TelegramSendSounds sound, CancellationToken cancellationToken)
            => Task.FromException<long?>(Refuse());

        public Task Edit_MessageTextWithButtonRows_Async(long messageId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, CancellationToken cancellationToken)
            => Task.FromException(Refuse());

        public Task<long> Create_ForumTopic_Async(string topicName, int? iconColor, CancellationToken cancellationToken) => _inner.Create_ForumTopic_Async(topicName, iconColor, cancellationToken);
        public Task Edit_ForumTopic_Async(long messageThreadId, string newName, CancellationToken cancellationToken) => _inner.Edit_ForumTopic_Async(messageThreadId, newName, cancellationToken);
        public Task Edit_GeneralForumTopic_Async(string newName, CancellationToken cancellationToken) => _inner.Edit_GeneralForumTopic_Async(newName, cancellationToken);
        public Task Delete_ForumTopic_Async(long messageThreadId, CancellationToken cancellationToken) => _inner.Delete_ForumTopic_Async(messageThreadId, cancellationToken);
        public Task Remove_TopicCreationPin_Async(long messageThreadId, CancellationToken cancellationToken) => _inner.Remove_TopicCreationPin_Async(messageThreadId, cancellationToken);
        public Task<long?> Send_Message_Async(long? messageThreadId, string text, TelegramSendSounds sound, CancellationToken cancellationToken) => _inner.Send_Message_Async(messageThreadId, text, sound, cancellationToken);
        public Task<long?> Send_HtmlMessage_Async(long? messageThreadId, string html, TelegramSendSounds sound, CancellationToken cancellationToken) => _inner.Send_HtmlMessage_Async(messageThreadId, html, sound, cancellationToken);
        public Task Send_TypingAction_Async(long? messageThreadId, CancellationToken cancellationToken) => _inner.Send_TypingAction_Async(messageThreadId, cancellationToken);
        public Task<long?> Send_HtmlMessageWithButtons_Async(long? messageThreadId, string html, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken) => _inner.Send_HtmlMessageWithButtons_Async(messageThreadId, html, buttons, sound, cancellationToken);
        public Task Edit_MessageText_Async(long messageId, string text, CancellationToken cancellationToken) => _inner.Edit_MessageText_Async(messageId, text, cancellationToken);
        public Task Edit_HtmlMessageText_Async(long messageId, string html, CancellationToken cancellationToken) => _inner.Edit_HtmlMessageText_Async(messageId, html, cancellationToken);
        public Task Edit_MessageTextWithButtons_Async(long messageId, string text, IReadOnlyList<(string Data, string Label)> buttons, CancellationToken cancellationToken) => _inner.Edit_MessageTextWithButtons_Async(messageId, text, buttons, cancellationToken);
        public Task<long?> Send_MessageWithButtons_Async(long? messageThreadId, string text, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken) => _inner.Send_MessageWithButtons_Async(messageThreadId, text, buttons, sound, cancellationToken);
        public Task Answer_CallbackQuery_Async(string callbackQueryId, string text, CancellationToken cancellationToken) => _inner.Answer_CallbackQuery_Async(callbackQueryId, text, cancellationToken);
        public Task Remove_MessageButtons_Async(long messageId, CancellationToken cancellationToken) => _inner.Remove_MessageButtons_Async(messageId, cancellationToken);
        public Task Delete_Message_Async(long messageId, CancellationToken cancellationToken) => _inner.Delete_Message_Async(messageId, cancellationToken);
        public Task Send_Photo_Async(long? messageThreadId, string filePath, TelegramSendSounds sound, CancellationToken cancellationToken) => _inner.Send_Photo_Async(messageThreadId, filePath, sound, cancellationToken);
        public Task Send_Document_Async(long? messageThreadId, string fileName, byte[] content, string captionHtml, TelegramSendSounds sound, CancellationToken cancellationToken) => _inner.Send_Document_Async(messageThreadId, fileName, content, captionHtml, sound, cancellationToken);
        public Task Set_MyCommands_Async(IReadOnlyList<(string Command, string Description)> commands, CancellationToken cancellationToken) => _inner.Set_MyCommands_Async(commands, cancellationToken);
        public Task Set_ChatMenuButton_ToCommands_Async(CancellationToken cancellationToken) => _inner.Set_ChatMenuButton_ToCommands_Async(cancellationToken);
        public Task<string> Get_UpdatesJson_Async(long offset, int timeoutSeconds, CancellationToken cancellationToken) => _inner.Get_UpdatesJson_Async(offset, timeoutSeconds, cancellationToken);
        public Task<string> Get_BotUsername_Async(CancellationToken cancellationToken) => _inner.Get_BotUsername_Async(cancellationToken);
        public Task Delete_Webhook_Async(bool dropPendingUpdates, CancellationToken cancellationToken) => _inner.Delete_Webhook_Async(dropPendingUpdates, cancellationToken);
        public Task Set_MessageReaction_Async(long messageId, string? emoji, CancellationToken cancellationToken) => _inner.Set_MessageReaction_Async(messageId, emoji, cancellationToken);
        public Task<byte[]> Download_File_Async(string fileId, CancellationToken cancellationToken) => _inner.Download_File_Async(fileId, cancellationToken);
    }
}
