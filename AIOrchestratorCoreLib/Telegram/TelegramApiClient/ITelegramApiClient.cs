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

    Task Send_Message_Async(long? messageThreadId, string text, CancellationToken cancellationToken);
    Task<string> Get_UpdatesJson_Async(long offset, int timeoutSeconds, CancellationToken cancellationToken);

    /// <summary>Downloads a file the owner sent (getFile + file endpoint) — screenshots of bugs, etc.</summary>
    Task<byte[]> Download_File_Async(string fileId, CancellationToken cancellationToken);
}
