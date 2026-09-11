namespace AIOrchestratorCoreLib.Telegram.TelegramApiClient;

/// <summary>
/// Thin Telegram Bot API adapter. Only the three calls the bridge needs; all higher logic
/// (chunking, filtering, routing) lives in pure, tested components.
/// </summary>
public interface ITelegramApiClient
{
    /// <param name="iconColor">
    /// One of Telegram's six permitted <c>icon_color</c> values (see
    /// <see cref="TopicColor_Rotation.PALETTE"/>), or null for its default. An explicit parameter
    /// rather than an optional one: a topic's colour is fixed at creation and can never be changed
    /// afterwards, so every call site has to have decided.
    /// </param>
    Task<long> Create_ForumTopic_Async(string topicName, int? iconColor, CancellationToken cancellationToken);

    /// <summary>Renames a topic (used when the supervisor sets the short goal name).</summary>
    Task Edit_ForumTopic_Async(long messageThreadId, string newName, CancellationToken cancellationToken);

    /// <summary>
    /// Renames the forum's GENERAL topic. Its own Bot API call, not the one above with a special id:
    /// the General topic carries no message_thread_id — the absence is exactly how this app tells it
    /// apart from every other topic — so there is no id to pass. Needs can_manage_topics on the bot.
    /// </summary>
    Task Edit_GeneralForumTopic_Async(string newName, CancellationToken cancellationToken);

    /// <summary>Deletes a topic AND its messages — closed orchestrations disappear from Telegram entirely.</summary>
    Task Delete_ForumTopic_Async(long messageThreadId, CancellationToken cancellationToken);

    /// <summary>
    /// Telegram auto-pins the "topic created" service message on bot-created topics — the owner
    /// wants no pins. Unpins everything in the topic and best-effort deletes the service message.
    /// </summary>
    Task Remove_TopicCreationPin_Async(long messageThreadId, CancellationToken cancellationToken);

    /// <summary>Returns the sent message's id (null when Telegram's reply carried none), so it can be edited later.</summary>
    /// <summary>
    /// EVERY SEND STATES WHETHER IT RINGS — <see cref="TelegramSendSounds"/> is required, never
    /// defaulted, so a loud message is a decision at the call site. The owner's ruling, 2026-09-09:
    /// the supervisor's own words ring; status, receipts and app bookkeeping do not. Text sends also
    /// disable Telegram's link preview unconditionally.
    /// </summary>
    Task<long?> Send_Message_Async(long? messageThreadId, string text, TelegramSendSounds sound, CancellationToken cancellationToken);

    /// <summary>
    /// Sends HTML-formatted text — every piece of agent-written prose, so the Markdown agents write
    /// arrives rendered instead of literal. Telegram rejects malformed HTML, so the caller must
    /// escape everything; <see cref="TelegramHtml_Renderer"/> is the one place that does.
    /// </summary>
    Task<long?> Send_HtmlMessage_Async(long? messageThreadId, string html, TelegramSendSounds sound, CancellationToken cancellationToken);

    /// <summary>
    /// Telegram's own "is typing…" bubble (<c>sendChatAction</c>). It is not a message: nothing lands
    /// in the topic and nobody is notified. The client shows it in the chat header for about five
    /// seconds and clears it the moment the bot's next message arrives, so a wait longer than that
    /// re-sends it on a cadence.
    /// </summary>
    Task Send_TypingAction_Async(long? messageThreadId, CancellationToken cancellationToken);

    /// <summary>
    /// The HTML send WITH an inline keyboard — the decision message, whose question text is written
    /// by an agent and whose buttons are not. Only the text is parsed; a button label is never HTML.
    /// </summary>
    Task<long?> Send_HtmlMessageWithButtons_Async(long? messageThreadId, string html, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken);

    /// <summary>
    /// Rewrites an already-sent message. Used for the delivery receipt, which EVOLVES in place
    /// (✓ → ✓✓ → ✓✓ · handoff) instead of stacking three messages in the topic.
    /// </summary>
    Task Edit_MessageText_Async(long messageId, string text, CancellationToken cancellationToken);

    /// <summary>
    /// The same rewrite, parsed as HTML. Its own method rather than a flag on the one above for the
    /// reason stated throughout this interface: a mode reachable by accident is a mode that will be
    /// reached by accident, and here the accident is Telegram refusing the edit outright.
    /// </summary>
    Task Edit_HtmlMessageText_Async(long messageId, string html, CancellationToken cancellationToken);

    /// <summary>
    /// Rewrites a message AND its buttons. Distinct from the plain edit because that one sends no
    /// reply_markup, which Telegram reads as "remove the keyboard" — right for a question whose
    /// answer has been recorded, wrong for a receipt whose button must change from ⏸ Wait to ▶ GO.
    /// </summary>
    Task Edit_MessageTextWithButtons_Async(long messageId, string text, IReadOnlyList<(string Data, string Label)> buttons, CancellationToken cancellationToken);

    /// <summary>
    /// The same edit, but the caller lays the buttons out in ROWS. The one-per-row shape above is
    /// right for decisions, whose labels are sentences; it is wrong for the topic's standing command
    /// bar, where four short commands stacked vertically would be a slab of buttons under a status
    /// line the owner reads all day.
    /// </summary>
    Task Edit_MessageTextWithButtonRows_Async(long messageId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, CancellationToken cancellationToken);

    /// <summary>
    /// sendMessage with an inline keyboard — one tappable button per (data, label) pair. Returns
    /// the message id so a tap can rewrite it to show WHICH option was chosen.
    /// </summary>
    Task<long?> Send_MessageWithButtons_Async(long? messageThreadId, string text, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken);

    /// <summary>The same send, with the buttons laid out in ROWS — see the row-aware edit above.</summary>
    Task<long?> Send_MessageWithButtonRows_Async(long? messageThreadId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, TelegramSendSounds sound, CancellationToken cancellationToken);

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
    Task Send_Photo_Async(long? messageThreadId, string filePath, TelegramSendSounds sound, CancellationToken cancellationToken);

    /// <summary>
    /// Uploads BYTES as a document (multipart sendDocument) — an owner-facing entry too long to read
    /// comfortably as chat messages, attached as the Markdown file it already is.
    ///
    /// <para>
    /// IN MEMORY, never from a path, and that is the difference from the photo above: the content is
    /// an entry the bridge is holding, not a file on disk, and writing it to a temp file to upload it
    /// would put owner-facing prose on the filesystem for no reason and leave it there on any throw
    /// between the write and the delete.
    /// </para>
    /// <para>
    /// The caption is HTML — <see cref="TelegramHtml_Renderer"/> renders it, like every other piece of
    /// agent-written prose — and Telegram caps it at 1024 characters after entity parsing.
    /// </para>
    /// </summary>
    Task Send_Document_Async(long? messageThreadId, string fileName, byte[] content, string captionHtml, TelegramSendSounds sound, CancellationToken cancellationToken);

    /// <summary>Registers the bot's command menu (the chat's ☰ menu button).</summary>
    Task Set_MyCommands_Async(IReadOnlyList<(string Command, string Description)> commands, CancellationToken cancellationToken);

    /// <summary>
    /// Pins the chat's MENU BUTTON to the commands list — the `/` in the message box. Registering
    /// commands is NOT enough on its own; without this the button is left to the client's default.
    /// </summary>
    Task Set_ChatMenuButton_ToCommands_Async(CancellationToken cancellationToken);

    Task<string> Get_UpdatesJson_Async(long offset, int timeoutSeconds, CancellationToken cancellationToken);

    /// <summary>
    /// The bot's own username (`getMe`), or the empty string when the response carries none.
    ///
    /// <para>
    /// IT IS CALLED ONCE, AT STARTUP, AND ITS VALUE IS A NAME FOR THE OWNER. When two hosts poll
    /// one token the collision is reported to them, and "another bridge is polling as @xyz_bot"
    /// tells them WHICH bot — they have more than one, and the answer decides which host to stop.
    /// It doubles as the first proof that the token in secrets.json is valid at all.
    /// </para>
    /// </summary>
    Task<string> Get_BotUsername_Async(CancellationToken cancellationToken);

    /// <summary>
    /// Clears any WEBHOOK registered against this token (`deleteWebhook`).
    ///
    /// <para>
    /// A WEBHOOK MAKES `getUpdates` FAIL FOREVER WITH 409 — Telegram allows one delivery mechanism
    /// per token, so a webhook left behind by an experiment, or by another tool sharing the token,
    /// looks exactly like a second poller and cannot be fixed from the app without this call.
    /// </para>
    /// <para>
    /// <paramref name="dropPendingUpdates"/> is passed FALSE by the bridge: whatever the owner sent
    /// while the webhook was in the way is still theirs, and dropping it would be the silent loss
    /// this brief exists to remove.
    /// </para>
    /// </summary>
    Task Delete_Webhook_Async(bool dropPendingUpdates, CancellationToken cancellationToken);

    /// <summary>
    /// Puts ONE reaction on a message, or clears it when <paramref name="emoji"/> is null
    /// (<c>setMessageReaction</c>, Bot API 7.0+) — the receipt that costs no line in the topic.
    ///
    /// <para>
    /// A bot may set exactly one, and only from a fixed set: see
    /// <see cref="OwnerReaction_Emoji"/>, which also records that <c>✅</c> is not in it. Telegram
    /// answers an unreactable message or a disallowed emoji with a 400, so every caller must have a
    /// fallback — brief D's is the silent ✓ message this replaces.
    /// </para>
    /// </summary>
    Task Set_MessageReaction_Async(long messageId, string? emoji, CancellationToken cancellationToken);

    /// <summary>Downloads a file the owner sent (getFile + file endpoint) — screenshots of bugs, etc.</summary>
    Task<byte[]> Download_File_Async(string fileId, CancellationToken cancellationToken);
}
