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
    /// The file the owner attached as a DOCUMENT, or null. See <see cref="TelegramDocumentRef"/> —
    /// until this field existed, a document with no caption produced no owner message at all and
    /// the offset advanced over it.
    /// </summary>
    TelegramDocumentRef? Document { get; }

    /// <summary>
    /// TRUE when the APP composed this message rather than the owner typing it: a tapped option, a
    /// released high-risk read-back, an applied deadline default. The text is real and goes to the
    /// session exactly as a typed message would — what it must never do is act as a TYPED ANSWER to
    /// some other open question.
    ///
    /// <para>
    /// THIS IS THE DEFECT IT CLOSES, and it was not confined to one button. Every tap is routed as
    /// an owner message, and the routing runs <c>Close_AnsweredQuestions_Async</c>, which binds a
    /// message to a question whenever exactly one is open. The tapped question is removed before
    /// routing, so "exactly one other open" is the common case — on 2026-09-09 at 17:53 the owner
    /// tapped a high-risk option on one question and at 17:55 tapped "Let's talk" on another; the
    /// talk text bound to the orphaned-processes question and stamped it <c>✅ answered:</c>. Nobody
    /// ever decided it. The log recorded a second question going out while one was still open three
    /// times that afternoon, so the shape was routine, not exotic.
    /// </para>
    /// <para>
    /// A FLAG ON THE MESSAGE RATHER THAN A SECOND ROUTE, deliberately: the whole point of routing a
    /// tap as an owner message is that everything downstream — aggregation, receipts, away mode,
    /// the delivery target — treats it identically. A parallel route would be a second copy of that
    /// pipeline, and the binding is the ONE step that must differ.
    /// </para>
    /// </summary>
    bool IsAppComposed { get; }

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
