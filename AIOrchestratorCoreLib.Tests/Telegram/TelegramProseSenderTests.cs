using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Logging.OrchestrationLogEntry;
using AIOrchestratorCoreLib.Mirroring;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// The send path's two rules, at the level where they are decided rather than end to end.
///
/// <para>
/// A 400 FALLS BACK, EVERYTHING ELSE DOES NOT — and the second half is the one that would be got
/// wrong by a "complete the sweep" pass. A 400 is Telegram refusing the request on its merits, so
/// the identical request will be refused again and only different CONTENT can succeed. A 429 or a
/// 5xx or a timeout means the outcome is UNKNOWN: the message may well have been posted, and a
/// second live call there posts it twice and spends another ~90 s inside a 2 s tick. Those belong to
/// the caller's own retry, which already exists.
/// </para>
/// </summary>
public class TelegramProseSenderTests
{
    const string MARKDOWN = "**bold** and `code`";
    const string RENDERED = "<b>bold</b> and <code>code</code>";

    [Fact]
    public async Task Send_RendersTheMarkdown_AndUsesTheHtmlCall()
    {
        var client = new ScriptedTelegram_Fake();
        var log = new CollectingLog_Fake();

        var messageId = await TelegramProse_Sender.Send_Async(client, log, "orch-1", 99, MARKDOWN, TelegramSendSounds.Rings, CancellationToken.None);

        Assert.Equal(RENDERED, Assert.Single(client.HtmlSends));
        Assert.Empty(client.PlainSends);
        Assert.NotNull(messageId);
    }

    [Fact]
    public async Task Send_WhenTelegramCannotParseTheEntities_ResendsThePlainMarkdown_Once()
    {
        var client = new ScriptedTelegram_Fake { HtmlFailure = Parse_Refusal() };
        var log = new CollectingLog_Fake();

        await TelegramProse_Sender.Send_Async(client, log, "orch-1", 99, MARKDOWN, TelegramSendSounds.Rings, CancellationToken.None);

        // THE ORIGINAL MARKDOWN, not the HTML. The owner then reads what they read before this
        // renderer existed — markers and all — which is a bad message, not a missing one.
        Assert.Equal(MARKDOWN, Assert.Single(client.PlainSends));
        Assert.Equal(1, client.HtmlAttempts);
    }

    [Fact]
    public async Task Send_TheFallbackLogsOneLine_NamingTheRefusalAndWhatWasDone()
    {
        var client = new ScriptedTelegram_Fake { HtmlFailure = Parse_Refusal() };
        var log = new CollectingLog_Fake();

        await TelegramProse_Sender.Send_Async(client, log, "orch-7", 99, MARKDOWN, TelegramSendSounds.Rings, CancellationToken.None);

        var line = Assert.Single(log.Warnings);

        Assert.Equal("orch-7", line.OrchId);
        Assert.Contains("can't parse entities", line.Message, StringComparison.Ordinal);
        Assert.Contains("resending as plain text", line.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    public async Task Send_ARetryableStatus_IsHandedToTheCaller_WithNoSecondLiveCall(int statusCode)
    {
        var client = new ScriptedTelegram_Fake { HtmlFailure = new TelegramApiException(statusCode, "later") };
        var log = new CollectingLog_Fake();

        await Assert.ThrowsAsync<TelegramApiException>(
            () => TelegramProse_Sender.Send_Async(client, log, "orch-1", 99, MARKDOWN, TelegramSendSounds.Rings, CancellationToken.None));

        Assert.Empty(client.PlainSends);
        Assert.Empty(log.Warnings);
    }

    [Fact]
    public async Task Send_ACancelledToken_IsNeverMistakenForARefusal()
    {
        var client = new ScriptedTelegram_Fake { HtmlThrowsCancellation = true };
        var log = new CollectingLog_Fake();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => TelegramProse_Sender.Send_Async(client, log, "orch-1", 99, MARKDOWN, TelegramSendSounds.Rings, CancellationToken.None));

        Assert.Empty(client.PlainSends);
    }

    [Fact]
    public async Task SendWithButtons_TheFallbackKeepsTheKeyboard()
    {
        var client = new ScriptedTelegram_Fake { HtmlFailure = Parse_Refusal() };
        var log = new CollectingLog_Fake();
        IReadOnlyList<(string Data, string Label)> buttons = [("d1", "Yes"), ("d2", "No")];

        await TelegramProse_Sender.Send_WithButtons_Async(client, log, "orch-1", 99, MARKDOWN, buttons, TelegramSendSounds.Rings, CancellationToken.None);

        // A DECISION MESSAGE WITHOUT ITS BUTTONS IS A QUESTION THE OWNER CANNOT ANSWER. Dropping to
        // the plain send would have stripped the keyboard, which is worse than the markers.
        Assert.Equal(MARKDOWN, Assert.Single(client.PlainSends));
        Assert.Equal(2, client.LastPlainButtonCount);
    }

    [Fact]
    public async Task Edit_RendersTheMarkdown_AndFallsBackTheSameWay()
    {
        var rendering = new ScriptedTelegram_Fake();
        await TelegramProse_Sender.Edit_Async(rendering, new CollectingLog_Fake(), "orch-1", 5, MARKDOWN, CancellationToken.None);

        Assert.Equal(RENDERED, Assert.Single(rendering.HtmlEdits));

        var refusing = new ScriptedTelegram_Fake { HtmlFailure = Parse_Refusal() };
        await TelegramProse_Sender.Edit_Async(refusing, new CollectingLog_Fake(), "orch-1", 5, MARKDOWN, CancellationToken.None);

        Assert.Equal(MARKDOWN, Assert.Single(refusing.PlainEdits));
    }

    /// <summary>
    /// THE FOLD'S FALLBACK IS THE UNFOLDED TEXT, and this is the rule that keeps the fold honest.
    ///
    /// <para>
    /// A folded message's HTML has no Markdown source — <c>&lt;blockquote expandable&gt;</c> is
    /// something the bridge assembled, not something an agent typed. So the send carries BOTH
    /// readings, and a 400 costs the FOLD rather than the message: what goes out is the piece exactly
    /// as stage 1i sent it, plain and whole. Re-sending the HTML would put the tags on the owner's
    /// screen, and re-rendering it would earn the same refusal a second time.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SendRendered_WhenTelegramRefusesTheFold_ResendsTheUNFOLDEDText_NeverTheTags()
    {
        var text = "THE SWEEP\n" + string.Join('\n', Enumerable.Range(1, 40).Select(i => $"finding {i} — and what it costs"));
        var piece = OwnerMessage_Folder.Fold_ForOwner(text)[0];

        Assert.Contains("<blockquote expandable>", piece.Html, StringComparison.Ordinal);

        var client = new ScriptedTelegram_Fake { HtmlFailure = Parse_Refusal() };
        var log = new CollectingLog_Fake();

        await TelegramProse_Sender.Send_Rendered_Async(
            client, log, "orch-1", 99, piece.Html, piece.Markdown, TelegramSendSounds.Rings, CancellationToken.None);

        var sent = Assert.Single(client.PlainSends);

        Assert.Equal(piece.Markdown, sent);
        Assert.DoesNotContain("blockquote", sent, StringComparison.Ordinal);
        Assert.Contains("THE SWEEP", sent, StringComparison.Ordinal);
        Assert.Contains("finding 40", sent, StringComparison.Ordinal);
        Assert.Equal(1, client.HtmlAttempts);
    }

    /// <summary>The unfolded path is the same call, and it must still send exactly the Markdown.</summary>
    [Fact]
    public async Task SendRendered_IsWhatSendAsyncIsBuiltFrom_SoAShortEntryFallsBackToItsOwnMarkdown()
    {
        var client = new ScriptedTelegram_Fake { HtmlFailure = Parse_Refusal() };

        await TelegramProse_Sender.Send_Rendered_Async(
            client, new CollectingLog_Fake(), "orch-1", 99, RENDERED, MARKDOWN, TelegramSendSounds.Rings, CancellationToken.None);

        Assert.Equal(MARKDOWN, Assert.Single(client.PlainSends));
    }

    static TelegramApiException Parse_Refusal()
    {
        return new TelegramApiException(
            400,
            "Telegram 'sendMessage' failed with HTTP 400: {\"ok\":false,\"description\":\"Bad Request: can't parse entities: Unmatched end tag at byte offset 7\"}");
    }
}

/// <summary>
/// Answers however the test tells it to, and remembers WHICH call each piece of text arrived on —
/// the only thing that distinguishes a rendered send from a plain one, since the interface expresses
/// <c>parse_mode</c> as a choice of method.
/// </summary>
internal sealed class ScriptedTelegram_Fake : ITelegramApiClient
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

    public TelegramApiException? HtmlFailure { get; set; }

    /// <summary>Stands in for an <c>HttpClient</c> timeout, which arrives as a cancellation.</summary>
    public bool HtmlThrowsCancellation { get; set; }

    public List<string> HtmlSends { get; } = [];
    public List<string> PlainSends { get; } = [];
    public List<string> HtmlEdits { get; } = [];
    public List<string> PlainEdits { get; } = [];
    public int HtmlAttempts { get; private set; }
    public int LastPlainButtonCount { get; private set; }

    public Task<long?> Send_HtmlMessage_Async(long? messageThreadId, string html, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        HtmlAttempts++;

        if (HtmlThrowsCancellation)
            throw new OperationCanceledException();

        if (HtmlFailure != null)
            throw HtmlFailure;

        HtmlSends.Add(html);

        return Task.FromResult<long?>(11);
    }

    public Task<long?> Send_HtmlMessageWithButtons_Async(long? messageThreadId, string html, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        return Send_HtmlMessage_Async(messageThreadId, html, sound, cancellationToken);
    }

    public Task<long?> Send_Message_Async(long? messageThreadId, string text, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        PlainSends.Add(text);

        return Task.FromResult<long?>(12);
    }

    public Task<long?> Send_MessageWithButtons_Async(long? messageThreadId, string text, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        LastPlainButtonCount = buttons.Count;

        return Send_Message_Async(messageThreadId, text, sound, cancellationToken);
    }

    public Task Edit_HtmlMessageText_Async(long messageId, string html, CancellationToken cancellationToken)
    {
        HtmlAttempts++;

        if (HtmlThrowsCancellation)
            throw new OperationCanceledException();

        if (HtmlFailure != null)
            throw HtmlFailure;

        HtmlEdits.Add(html);

        return Task.CompletedTask;
    }

    public Task Edit_MessageText_Async(long messageId, string text, CancellationToken cancellationToken)
    {
        PlainEdits.Add(text);

        return Task.CompletedTask;
    }

    public Task<long?> Send_MessageWithButtonRows_Async(long? messageThreadId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        return Send_Message_Async(messageThreadId, text, sound, cancellationToken);
    }

    public Task Edit_MessageTextWithButtons_Async(long messageId, string text, IReadOnlyList<(string Data, string Label)> buttons, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Edit_MessageTextWithButtonRows_Async(long messageId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<long> Create_ForumTopic_Async(string topicName, int? iconColor, CancellationToken cancellationToken) => Task.FromResult(1L);

    public Task Edit_ForumTopic_Async(long messageThreadId, string newName, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Edit_GeneralForumTopic_Async(string newName, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Delete_ForumTopic_Async(long messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Remove_TopicCreationPin_Async(long messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Answer_CallbackQuery_Async(string callbackQueryId, string text, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Remove_MessageButtons_Async(long messageId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Delete_Message_Async(long messageId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Send_Photo_Async(long? messageThreadId, string filePath, TelegramSendSounds sound, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Send_Document_Async(long? messageThreadId, string fileName, byte[] content, string captionHtml, TelegramSendSounds sound, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Set_MyCommands_Async(IReadOnlyList<(string Command, string Description)> commands, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Set_ChatMenuButton_ToCommands_Async(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<string> Get_UpdatesJson_Async(long offset, int timeoutSeconds, CancellationToken cancellationToken) => Task.FromResult("{\"ok\":true,\"result\":[]}");

    public Task Set_MessageReaction_Async(long messageId, string? emoji, CancellationToken cancellationToken) => Task.CompletedTask;


    public Task<byte[]> Download_File_Async(string fileId, CancellationToken cancellationToken) => Task.FromResult(Array.Empty<byte>());
}

/// <summary>Keeps the orch id with each line, because "which orchestration" is half the log line.</summary>
internal sealed class CollectingLog_Fake : IOrchestrationLog
{
    public List<(string OrchId, string Message)> Warnings { get; } = [];

    public void Log_Info(string orchId, string message)
    {
    }

    public void Log_Warning(string orchId, string message)
    {
        Warnings.Add((orchId, message));
    }

    public void Log_Error(string orchId, string message, Exception? exception)
    {
    }

    public event Action<IOrchestrationLogEntry>? EntryLogged
    {
        add { }
        remove { }
    }
}
