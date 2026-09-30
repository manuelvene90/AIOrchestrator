using AIOrchestratorCoreLib.Telegram.TopicTraffic;

namespace AIOrchestratorCoreLib.Telegram.TelegramApiClient;

/// <summary>
/// THE ONE CHOKEPOINT THROUGH WHICH EVERY MESSAGE THE APP PUTS IN A TOPIC IS RECORDED AS TOPIC TRAFFIC
/// (owner, 2026-09-30) — a decorator around the real client, so a send is recorded by being a send.
///
/// <para>
/// WHY A DECORATOR AND NOT A CALL AT EACH SITE. The status line is moved back to the bottom only when
/// the app knows it has been buried, and until 2026-09-30 the app learned that from
/// <c>Remember_TopicMessage</c>, called by hand at six send sites of the engine. At least a dozen others
/// never called it — alerts, entry documents, photos and attachments, screenshots, the undelivered
/// digest, the hold receipt, the busy narration, the delivery receipts — so each of them buried PULSE
/// silently and the owner found it twenty messages up. A thirteenth hand-placed call would have been
/// the fourteenth missing one next month. Every component the engine hands a client to is handed THIS
/// one, so there is no send left to forget.
/// </para>
/// <para>
/// ONLY SENDS ARE RECORDED, and only once they succeed: an edit, a reaction, a delete or the typing
/// bubble puts nothing new at the bottom of a topic, and a send that threw put nothing anywhere. The
/// status line's OWN post is recorded like any other message, which is harmless because an equal id
/// is not burial (<see cref="TopicStatusLine_Planner.Is_Buried"/>).
/// </para>
/// <para>
/// EVERY MEMBER IS FORWARDED EXPLICITLY, the two default-implemented reply sends included: forwarding
/// them to the inner client's own members keeps a client that overrides them (the real one threads a
/// reply) threading, where inheriting the interface default would quietly send unthreaded.
/// </para>
/// </summary>
internal sealed class TopicTrafficRecordingClientModel(ITelegramApiClient inner, ITopicTraffic traffic) : ITelegramApiClient
{
    readonly ITelegramApiClient _inner = inner;
    readonly ITopicTraffic _traffic = traffic;

    /// <summary>Records what a send put in the topic and hands its id back untouched.</summary>
    long? Record(long? messageThreadId, long? messageId)
    {
        if (messageId == null)
            _traffic.Note_UnidentifiedMessage(messageThreadId);
        else
            _traffic.Note_Message(messageThreadId, messageId.Value);

        return messageId;
    }

    // ── SENDS: recorded ────────────────────────────────────────────────────────────────────────────

    public async Task<long?> Send_Message_Async(long? messageThreadId, string text, TelegramSendSounds sound, CancellationToken cancellationToken)
        => Record(messageThreadId, await _inner.Send_Message_Async(messageThreadId, text, sound, cancellationToken));

    public async Task<long?> Send_HtmlMessage_Async(long? messageThreadId, string html, TelegramSendSounds sound, CancellationToken cancellationToken)
        => Record(messageThreadId, await _inner.Send_HtmlMessage_Async(messageThreadId, html, sound, cancellationToken));

    public async Task<long?> Send_HtmlReply_Async(long? messageThreadId, string html, long replyToMessageId, TelegramSendSounds sound, CancellationToken cancellationToken)
        => Record(messageThreadId, await _inner.Send_HtmlReply_Async(messageThreadId, html, replyToMessageId, sound, cancellationToken));

    public async Task<long?> Send_MessageReply_Async(long? messageThreadId, string text, long replyToMessageId, TelegramSendSounds sound, CancellationToken cancellationToken)
        => Record(messageThreadId, await _inner.Send_MessageReply_Async(messageThreadId, text, replyToMessageId, sound, cancellationToken));

    public async Task<long?> Send_HtmlMessageWithButtons_Async(long? messageThreadId, string html, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken)
        => Record(messageThreadId, await _inner.Send_HtmlMessageWithButtons_Async(messageThreadId, html, buttons, sound, cancellationToken));

    public async Task<long?> Send_MessageWithButtons_Async(long? messageThreadId, string text, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken)
        => Record(messageThreadId, await _inner.Send_MessageWithButtons_Async(messageThreadId, text, buttons, sound, cancellationToken));

    public async Task<long?> Send_MessageWithButtonRows_Async(long? messageThreadId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, TelegramSendSounds sound, CancellationToken cancellationToken)
        => Record(messageThreadId, await _inner.Send_MessageWithButtonRows_Async(messageThreadId, text, buttonRows, sound, cancellationToken));

    public async Task Send_Photo_Async(long? messageThreadId, string filePath, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        await _inner.Send_Photo_Async(messageThreadId, filePath, sound, cancellationToken);
        _traffic.Note_UnidentifiedMessage(messageThreadId);
    }

    public async Task Send_Document_Async(long? messageThreadId, string fileName, byte[] content, string captionHtml, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        await _inner.Send_Document_Async(messageThreadId, fileName, content, captionHtml, sound, cancellationToken);
        _traffic.Note_UnidentifiedMessage(messageThreadId);
    }

    // ── EVERYTHING ELSE: forwarded, nothing recorded ─────────────────────────────────────────────

    public Task<long> Create_ForumTopic_Async(string topicName, int? iconColor, CancellationToken cancellationToken)
        => _inner.Create_ForumTopic_Async(topicName, iconColor, cancellationToken);

    public Task Edit_ForumTopic_Async(long messageThreadId, string newName, CancellationToken cancellationToken)
        => _inner.Edit_ForumTopic_Async(messageThreadId, newName, cancellationToken);

    public Task Edit_GeneralForumTopic_Async(string newName, CancellationToken cancellationToken)
        => _inner.Edit_GeneralForumTopic_Async(newName, cancellationToken);

    public Task Delete_ForumTopic_Async(long messageThreadId, CancellationToken cancellationToken)
        => _inner.Delete_ForumTopic_Async(messageThreadId, cancellationToken);

    public Task Close_ForumTopic_Async(long messageThreadId, CancellationToken cancellationToken)
        => _inner.Close_ForumTopic_Async(messageThreadId, cancellationToken);

    public Task Remove_TopicCreationPin_Async(long messageThreadId, CancellationToken cancellationToken)
        => _inner.Remove_TopicCreationPin_Async(messageThreadId, cancellationToken);

    public Task Send_TypingAction_Async(long? messageThreadId, CancellationToken cancellationToken)
        => _inner.Send_TypingAction_Async(messageThreadId, cancellationToken);

    public Task Edit_MessageText_Async(long messageId, string text, CancellationToken cancellationToken)
        => _inner.Edit_MessageText_Async(messageId, text, cancellationToken);

    public Task Edit_HtmlMessageText_Async(long messageId, string html, CancellationToken cancellationToken)
        => _inner.Edit_HtmlMessageText_Async(messageId, html, cancellationToken);

    public Task Edit_MessageTextWithButtons_Async(long messageId, string text, IReadOnlyList<(string Data, string Label)> buttons, CancellationToken cancellationToken)
        => _inner.Edit_MessageTextWithButtons_Async(messageId, text, buttons, cancellationToken);

    public Task Edit_MessageTextWithButtonRows_Async(long messageId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, CancellationToken cancellationToken)
        => _inner.Edit_MessageTextWithButtonRows_Async(messageId, text, buttonRows, cancellationToken);

    public Task Answer_CallbackQuery_Async(string callbackQueryId, string text, CancellationToken cancellationToken)
        => _inner.Answer_CallbackQuery_Async(callbackQueryId, text, cancellationToken);

    public Task Remove_MessageButtons_Async(long messageId, CancellationToken cancellationToken)
        => _inner.Remove_MessageButtons_Async(messageId, cancellationToken);

    public Task Delete_Message_Async(long messageId, CancellationToken cancellationToken)
        => _inner.Delete_Message_Async(messageId, cancellationToken);

    public Task Set_MyCommands_Async(IReadOnlyList<(string Command, string Description)> commands, CancellationToken cancellationToken)
        => _inner.Set_MyCommands_Async(commands, cancellationToken);

    public Task Set_ChatMenuButton_ToCommands_Async(CancellationToken cancellationToken)
        => _inner.Set_ChatMenuButton_ToCommands_Async(cancellationToken);

    public Task<string> Get_UpdatesJson_Async(long offset, int timeoutSeconds, CancellationToken cancellationToken)
        => _inner.Get_UpdatesJson_Async(offset, timeoutSeconds, cancellationToken);

    public Task<string> Get_BotUsername_Async(CancellationToken cancellationToken)
        => _inner.Get_BotUsername_Async(cancellationToken);

    public Task Delete_Webhook_Async(bool dropPendingUpdates, CancellationToken cancellationToken)
        => _inner.Delete_Webhook_Async(dropPendingUpdates, cancellationToken);

    public Task Set_MessageReaction_Async(long messageId, string? emoji, CancellationToken cancellationToken)
        => _inner.Set_MessageReaction_Async(messageId, emoji, cancellationToken);

    public Task<byte[]> Download_File_Async(string fileId, CancellationToken cancellationToken)
        => _inner.Download_File_Async(fileId, cancellationToken);
}
