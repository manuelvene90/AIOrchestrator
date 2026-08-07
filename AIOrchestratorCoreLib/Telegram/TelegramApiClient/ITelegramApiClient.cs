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

    Task Send_Message_Async(long? messageThreadId, string text, CancellationToken cancellationToken);

    /// <summary>sendMessage with an inline keyboard — one tappable button per (data, label) pair.</summary>
    Task Send_MessageWithButtons_Async(long? messageThreadId, string text, IReadOnlyList<(string Data, string Label)> buttons, CancellationToken cancellationToken);

    /// <summary>Answers a button tap (stops the phone-side spinner); text shows as a small toast.</summary>
    Task Answer_CallbackQuery_Async(string callbackQueryId, string text, CancellationToken cancellationToken);

    /// <summary>Uploads a local image file as a photo message (multipart sendPhoto).</summary>
    Task Send_Photo_Async(long? messageThreadId, string filePath, CancellationToken cancellationToken);

    /// <summary>Registers the bot's command menu (the chat's ☰ menu button).</summary>
    Task Set_MyCommands_Async(IReadOnlyList<(string Command, string Description)> commands, CancellationToken cancellationToken);

    Task<string> Get_UpdatesJson_Async(long offset, int timeoutSeconds, CancellationToken cancellationToken);

    /// <summary>Downloads a file the owner sent (getFile + file endpoint) — screenshots of bugs, etc.</summary>
    Task<byte[]> Download_File_Async(string fileId, CancellationToken cancellationToken);
}
