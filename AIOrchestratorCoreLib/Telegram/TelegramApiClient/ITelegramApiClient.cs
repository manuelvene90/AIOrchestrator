namespace AIOrchestratorCoreLib.Telegram.TelegramApiClient;

/// <summary>
/// Thin Telegram Bot API adapter. Only the three calls the bridge needs; all higher logic
/// (chunking, filtering, routing) lives in pure, tested components.
/// </summary>
public interface ITelegramApiClient
{
    Task<long> Create_ForumTopic_Async(string topicName, CancellationToken cancellationToken);

    /// <summary>Renames a topic (used when the supervisor sets the short goal name).</summary>
    Task Edit_ForumTopic_Async(long messageThreadId, string newName, CancellationToken cancellationToken);

    /// <summary>Deletes a topic AND its messages — closed orchestrations disappear from Telegram entirely.</summary>
    Task Delete_ForumTopic_Async(long messageThreadId, CancellationToken cancellationToken);

    /// <summary>
    /// Telegram auto-pins the "topic created" service message on bot-created topics — the owner
    /// wants no pins. Unpins everything in the topic and best-effort deletes the service message.
    /// </summary>
    Task Remove_TopicCreationPin_Async(long messageThreadId, CancellationToken cancellationToken);

    /// <summary>Returns the sent message's id (null when Telegram's reply carried none), so it can be edited later.</summary>
    Task<long?> Send_Message_Async(long? messageThreadId, string text, CancellationToken cancellationToken);

    /// <summary>
    /// Sends HTML-formatted text — used for ASCII mockups, which need a &lt;pre&gt; block to keep a
    /// monospaced font. Telegram rejects malformed HTML, so the caller must escape everything.
    /// </summary>
    Task<long?> Send_HtmlMessage_Async(long? messageThreadId, string html, CancellationToken cancellationToken);

    /// <summary>
    /// Rewrites an already-sent message. Used for the delivery receipt, which EVOLVES in place
    /// (✓ → ✓✓ → ✓✓ · handoff) instead of stacking three messages in the topic.
    /// </summary>
    Task Edit_MessageText_Async(long messageId, string text, CancellationToken cancellationToken);

    /// <summary>sendMessage with an inline keyboard — one tappable button per (data, label) pair.</summary>
    Task Send_MessageWithButtons_Async(long? messageThreadId, string text, IReadOnlyList<(string Data, string Label)> buttons, CancellationToken cancellationToken);

    /// <summary>Answers a button tap (stops the phone-side spinner); text shows as a small toast.</summary>
    Task Answer_CallbackQuery_Async(string callbackQueryId, string text, CancellationToken cancellationToken);

    /// <summary>Strips the inline keyboard from a sent message — decision buttons are SINGLE-USE.</summary>
    Task Remove_MessageButtons_Async(long messageId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a message. Used on Telegram's own SERVICE messages ("X changed the topic name"),
    /// which a topic rename emits and which would otherwise litter the conversation.
    /// </summary>
    Task Delete_Message_Async(long messageId, CancellationToken cancellationToken);

    /// <summary>Uploads a local image file as a photo message (multipart sendPhoto).</summary>
    Task Send_Photo_Async(long? messageThreadId, string filePath, CancellationToken cancellationToken);

    /// <summary>Registers the bot's command menu (the chat's ☰ menu button).</summary>
    Task Set_MyCommands_Async(IReadOnlyList<(string Command, string Description)> commands, CancellationToken cancellationToken);

    Task<string> Get_UpdatesJson_Async(long offset, int timeoutSeconds, CancellationToken cancellationToken);

    /// <summary>Downloads a file the owner sent (getFile + file endpoint) — screenshots of bugs, etc.</summary>
    Task<byte[]> Download_File_Async(string fileId, CancellationToken cancellationToken);
}
