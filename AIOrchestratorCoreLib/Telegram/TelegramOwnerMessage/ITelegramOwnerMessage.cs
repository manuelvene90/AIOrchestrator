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

    /// <summary>
    /// The text of the message this one REPLIES to, when the owner used Telegram's reply to point at
    /// something. Null when they did not.
    ///
    /// IT IS NOT SIMPLY `reply_to_message`. In a forum supergroup EVERY message in a topic carries a
    /// reply pointing at the topic's root message, so taking the field at face value would attach
    /// phantom context to every message they ever send. A real reply is one whose target is not the
    /// thread root — see TelegramUpdates_Parser, where that test lives.
    /// </summary>
    string? ReplyToText { get; }
}
