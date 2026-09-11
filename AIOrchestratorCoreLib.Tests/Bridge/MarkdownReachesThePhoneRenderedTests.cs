using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;
using AIOrchestratorCoreLib.Tests.TestSupport;
using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE MARKERS WERE ON THE OWNER'S SCREEN.
///
/// Owner, 2026-09-07, from their phone: a supervisor's channel entry arrived reading
/// <c>**GOAL 2: V1 LIVE**</c> and <c>- **Milestone:** 11 aperte</c>. Agents write Markdown; the
/// mirror sent every entry through <c>sendMessage</c> with no <c>parse_mode</c>, so Telegram had
/// nothing to render and printed the source.
///
/// <para>
/// Two facts are pinned here, and the second is the one that matters most:
/// </para>
/// <list type="number">
///   <item>a mirrored entry reaches Telegram RENDERED and through the HTML call — nothing else in
///   this codebase expresses "parse_mode: HTML", the choice of method IS the parse mode;</item>
///   <item>when Telegram REFUSES to parse it, the message still arrives. A rendering bug must cost
///   the formatting and never the message: a 400 on the mirror path would otherwise be retried into
///   the same rejection for ever and wedge that channel.</item>
/// </list>
/// <para>
/// A THIRD fact used to live here — that the Italian layer ran before the renderer, so the
/// translated text was what got rendered. The layer was abolished on 2026-09-09 and the ordering it
/// pinned no longer exists.
/// </para>
/// <para>
/// Driven end to end rather than poked, because which API method the engine picks has no getter —
/// it is only observable as the call the client received.
/// </para>
/// </summary>
public class MarkdownReachesThePhoneRenderedTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 4242;

    /// <summary>
    /// The owner's own example, shortened so the assertion is exact — plus a closing question,
    /// because <c>OwnerPush_Policy</c> keeps pure narration off the phone entirely and an entry that
    /// never leaves would make every assertion below pass or fail for the wrong reason. No
    /// <c>OPTION:</c> lines, so this stays an ordinary mirrored message rather than a button one.
    /// </summary>
    const string MARKDOWN_ENTRY = "**GOAL 2: V1 LIVE**\nShall I proceed?";
    const string MARKDOWN_HEADLINE = "**GOAL 2: V1 LIVE**";
    const string RENDERED_ENTRY = "<b>GOAL 2: V1 LIVE</b>";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly RecordingLog_Fake _log;
    readonly ByMethodTelegram_Fake _telegram;
    readonly IOrchestratorConfigProvider _configProvider;

    public MarkdownReachesThePhoneRenderedTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-markdown-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        Write_Config();
        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _log = new RecordingLog_Fake();
        _telegram = new ByMethodTelegram_Fake();
        _configProvider = OrchestratorConfigProvider_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, _configProvider, _store, new RecordingSpawner_Fake(), _log);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that will not delete is not a test result.
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AMirroredEntry_ReachesTelegramAsHtml_WithItsMarkersRendered()
    {
        var engine = Build_Engine();
        var orchId = await Start_WithChannelAlreadySeen_Async(engine);

        Append_SupervisorEntry(orchId, 1, "goal", MARKDOWN_ENTRY);

        Assert.True(
            await Run_Until_Async(engine, () => _telegram.AnyHtmlSendContains(RENDERED_ENTRY), 20_000),
            "the entry never reached Telegram rendered."
                + $"{Environment.NewLine}html sends: {_telegram.Dump_HtmlSends()}"
                + $"{Environment.NewLine}plain sends: {_telegram.Dump_PlainSends()}"
                + $"{Environment.NewLine}{_log.Dump()}");

        // THE OTHER HALF, and it is not implied by the one above: an entry that went out BOTH ways
        // would satisfy the assertion while double-posting to the owner's topic.
        Assert.False(
            _telegram.AnyPlainSendContains("GOAL 2"),
            $"the entry ALSO went out as plain text: {_telegram.Dump_PlainSends()}");
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task WhenTelegramRefusesToParseIt_TheMessageStillArrives_AndTheLogNamesTheRefusal()
    {
        // Exactly what Telegram answers when an entity does not parse. The status is what the
        // fallback reads; the sentence is what makes the log line diagnosable.
        _telegram.Refuse_HtmlWith(new TelegramApiException(
            400, "Telegram 'sendMessage' failed with HTTP 400: {\"ok\":false,\"description\":\"Bad Request: can't parse entities: Unmatched end tag at byte offset 12\"}"));

        var engine = Build_Engine();
        var orchId = await Start_WithChannelAlreadySeen_Async(engine);

        Append_SupervisorEntry(orchId, 1, "goal", MARKDOWN_ENTRY);

        Assert.True(
            await Run_Until_Async(engine, () => _telegram.AnyPlainSendContains(MARKDOWN_HEADLINE), 20_000),
            "THE MESSAGE WAS LOST. Telegram refused the HTML and nothing was re-sent as plain text, "
                + "so a rendering bug costs the owner an entry — the one outcome this system exists "
                + $"to prevent.{Environment.NewLine}{_log.Dump()}");

        Assert.True(
            _log.Has_Line_Containing("can't parse entities"),
            "the fallback was silent about WHY it fired, which leaves a renderer bug undiagnosable "
                + $"after the fact.{Environment.NewLine}{_log.Dump()}");

        Assert.True(
            _log.Has_Line_Containing("resending as plain text"),
            $"the log line does not say what was done about it.{Environment.NewLine}{_log.Dump()}");
    }

    IBridgeEngine Build_Engine()
    {
        return BridgeEngine_Factory.Create_WithTelegramClient(
            _paths, _configProvider, _store, _launcher, _log, _telegram, BridgeTestTiming.Fast());
    }

    void Write_Config()
    {
        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID}}}");
    }

    /// <summary>
    /// The tailer registers a file it has never seen at its CURRENT END, so an entry appended before
    /// the first poll is behind the starting offset and never mirrors — a setup mistake that looks
    /// exactly like the defect under test. One short run baselines the file first.
    /// </summary>
    async Task<string> Start_WithChannelAlreadySeen_Async(IBridgeEngine engine)
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);

        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);
        _store.Set_DisplayName(session.OrchId, "IS · markdown");

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        await Run_Until_Async(engine, () => false, BridgeTestTiming.Window_ForTicks(3));

        _telegram.Forget_Everything();

        return session.OrchId;
    }

    void Append_SupervisorEntry(string orchId, int index, string subject, string body)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        File.AppendAllText(channelFile, $"\n## [{index}] FROM supervisor — {stamp} — {subject}\n{body}\n");
    }

    static async Task<bool> Run_Until_Async(IBridgeEngine engine, Func<bool> condition, int maxMilliseconds)
    {
        using var cancellation = new CancellationTokenSource();

        var loop = engine.Run_Async(cancellation.Token);
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

/// <summary>
/// Records WHICH call each message arrived on, because that is the only observable difference
/// between "sent as HTML" and "sent as plain text" — the interface expresses <c>parse_mode</c> as a
/// choice of method, so a fake that folded the two together could not see this defect at all.
/// </summary>
internal sealed class ByMethodTelegram_Fake : ITelegramApiClient
{

    // The startup handshake (see ITelegramApiClient): a fake not testing it answers with a name and
    // a cleared webhook, so the inbound loop starts exactly as it does in production.
    public Task<string> Get_BotUsername_Async(CancellationToken cancellationToken) => Task.FromResult("test_bot");

    public Task Delete_Webhook_Async(bool dropPendingUpdates, CancellationToken cancellationToken) => Task.CompletedTask;
    // The typing bubble is not this probe's subject; it creates no message, so it is not recorded.
    public Task Send_TypingAction_Async(long? messageThreadId, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    const string EMPTY_UPDATES = "{\"ok\":true,\"result\":[]}";

    readonly object _lock = new();
    readonly List<string> _htmlSends = [];
    readonly List<string> _plainSends = [];
    readonly List<(long? ThreadId, string FileName, byte[] Content, string CaptionHtml)> _documents = [];
    long _nextMessageId = 900;
    TelegramApiException? _htmlRefusal;

    /// <summary>Makes every HTML call answer the way Telegram answers an entity it cannot parse.</summary>
    public void Refuse_HtmlWith(TelegramApiException refusal)
    {
        lock (_lock)
            _htmlRefusal = refusal;
    }

    /// <summary>
    /// Drops the startup traffic (topic status lines, command bars) so the assertions below are
    /// about the entry under test and not about whatever the engine says on its way up.
    /// </summary>
    public void Forget_Everything()
    {
        lock (_lock)
        {
            _htmlSends.Clear();
            _plainSends.Clear();
            _documents.Clear();
        }
    }

    public bool AnyHtmlSendContains(string fragment)
    {
        lock (_lock)
            return _htmlSends.Any(text => text.Contains(fragment, StringComparison.Ordinal));
    }

    public bool AnyPlainSendContains(string fragment)
    {
        lock (_lock)
            return _plainSends.Any(text => text.Contains(fragment, StringComparison.Ordinal));
    }

    /// <summary>
    /// A snapshot of every HTML send, in the order Telegram received them — what a test about
    /// SPLITTING needs, where the older ones only ever asked whether a fragment appeared somewhere.
    /// </summary>
    public IReadOnlyList<string> Html_Sends()
    {
        lock (_lock)
            return [.. _htmlSends];
    }

    public string Dump_HtmlSends()
    {
        lock (_lock)
            return _htmlSends.Count == 0 ? "(none)" : string.Join(" | ", _htmlSends);
    }

    public string Dump_PlainSends()
    {
        lock (_lock)
            return _plainSends.Count == 0 ? "(none)" : string.Join(" | ", _plainSends);
    }

    public Task<long?> Send_Message_Async(long? messageThreadId, string text, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _plainSends.Add(text);

            return Task.FromResult<long?>(_nextMessageId++);
        }
    }

    public Task<long?> Send_HtmlMessage_Async(long? messageThreadId, string html, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_htmlRefusal != null)
                throw _htmlRefusal;

            _htmlSends.Add(html);

            return Task.FromResult<long?>(_nextMessageId++);
        }
    }

    public Task<long?> Send_HtmlMessageWithButtons_Async(long? messageThreadId, string html, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        return Send_HtmlMessage_Async(messageThreadId, html, sound, cancellationToken);
    }

    public Task<long?> Send_MessageWithButtons_Async(long? messageThreadId, string text, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        return Send_Message_Async(messageThreadId, text, sound, cancellationToken);
    }

    public Task<long?> Send_MessageWithButtonRows_Async(long? messageThreadId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        return Send_Message_Async(messageThreadId, text, sound, cancellationToken);
    }

    public Task Edit_MessageText_Async(long messageId, string text, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Edit_HtmlMessageText_Async(long messageId, string html, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Edit_MessageTextWithButtons_Async(long messageId, string text, IReadOnlyList<(string Data, string Label)> buttons, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Edit_MessageTextWithButtonRows_Async(long messageId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<long> Create_ForumTopic_Async(string topicName, int? iconColor, CancellationToken cancellationToken) => Task.FromResult(7777L);

    public Task Edit_ForumTopic_Async(long messageThreadId, string newName, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Edit_GeneralForumTopic_Async(string newName, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Delete_ForumTopic_Async(long messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Remove_TopicCreationPin_Async(long messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Answer_CallbackQuery_Async(string callbackQueryId, string text, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Remove_MessageButtons_Async(long messageId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Delete_Message_Async(long messageId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Send_Photo_Async(long? messageThreadId, string filePath, TelegramSendSounds sound, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// RECORDED WHOLE — name, bytes and caption — because "the entry was also attached" is only
    /// checkable against what Telegram actually received, and an attachment with the wrong name or a
    /// truncated body would satisfy a mere counter.
    /// </summary>
    public Task Send_Document_Async(long? messageThreadId, string fileName, byte[] content, string captionHtml, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        lock (_lock)
            _documents.Add((messageThreadId, fileName, content, captionHtml));

        return Task.CompletedTask;
    }

    public IReadOnlyList<(long? ThreadId, string FileName, byte[] Content, string CaptionHtml)> Documents()
    {
        lock (_lock)
            return [.. _documents];
    }

    public string Dump_Documents()
    {
        lock (_lock)
            return _documents.Count == 0 ? "(none)" : string.Join(" | ", _documents.Select(document => $"{document.FileName} ({document.Content.Length} bytes)"));
    }

    public Task Set_MyCommands_Async(IReadOnlyList<(string Command, string Description)> commands, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Set_ChatMenuButton_ToCommands_Async(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task<string> Get_UpdatesJson_Async(long offset, int timeoutSeconds, CancellationToken cancellationToken)
    {
        // Stands in for the long poll. Without it this loop spins hot for the whole run.
        await Task.Delay(50, cancellationToken);

        return EMPTY_UPDATES;
    }

    public Task Set_MessageReaction_Async(long messageId, string? emoji, CancellationToken cancellationToken) => Task.CompletedTask;


    public Task<byte[]> Download_File_Async(string fileId, CancellationToken cancellationToken) => Task.FromResult(Array.Empty<byte>());
}
