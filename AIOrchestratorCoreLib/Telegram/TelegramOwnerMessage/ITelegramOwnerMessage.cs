namespace AIOrchestratorCoreLib.Telegram.TelegramOwnerMessage;

/// <summary>A message the owner sent in the supervision supergroup, parsed from getUpdates.</summary>
public interface ITelegramOwnerMessage
{
    long UpdateId { get; }

    /// <summary>The message's own id — needed to delete it when the owner clears a topic.</summary>
    long? MessageId { get; }
    long ChatId { get; }
    long FromUserId { get; }

    /// <summary>Forum topic id; null when the message was sent outside any topic.</summary>
    long? MessageThreadId { get; }

    /// <summary>Message text, or the photo caption (possibly empty) for photo messages.</summary>
    string Text { get; }

    /// <summary>Telegram file id of the LARGEST photo size, when the owner sent an image.</summary>
    string? PhotoFileId { get; }

    /// <summary>Telegram file id of a voice note (.oga) the owner sent, transcribed if configured.</summary>
    string? VoiceFileId { get; }
}
