namespace AIOrchestratorCoreLib.Telegram.TelegramApiClient;

/// <summary>
/// WHICH ALLOWANCE A TELEGRAM CALL SPENDS — brief F5.
///
/// <para>
/// It was a <c>bool rateLimited</c>, which could express only "on the message bucket" and "on
/// nothing". The second value was doing two jobs: calls that genuinely must never queue
/// (<c>sendChatAction</c>, whose whole point is to be replaced by the next one) and calls that
/// merely must not spend the MESSAGE allowance (every edit, every delete, every callback answer) —
/// and the second group was the app's highest-volume traffic, metered by nothing at all.
/// </para>
/// </summary>
public enum TelegramCallClasses
{
    /// <summary>
    /// Never queued behind anything. <c>sendChatAction</c> (a refresh that arrives late is worse
    /// than one that never went), <c>getFile</c>, <c>setMyCommands</c>, <c>setChatMenuButton</c>,
    /// <c>createForumTopic</c> (once per orchestration, and a topic that does not exist yet blocks
    /// every message that would go into it).
    /// </summary>
    Unmetered,

    /// <summary>
    /// Creates a message in the group — what Telegram's published twenty-a-minute ceiling counts.
    /// <c>sendMessage</c> in all its shapes, <c>sendPhoto</c>, <c>sendDocument</c>.
    /// </summary>
    Message,

    /// <summary>
    /// Changes or removes something that already exists: <c>editMessageText</c>,
    /// <c>editMessageReplyMarkup</c>, <c>deleteMessage</c>, <c>answerCallbackQuery</c>,
    /// <c>editForumTopic</c>, <c>editGeneralForumTopic</c>, <c>deleteForumTopic</c>. Its own,
    /// larger bucket, and a much shorter 429 ceiling — see
    /// <see cref="TokenBucket_Gate.MAXIMUM_CONTROL_RETRY_WAIT"/>.
    /// </summary>
    Control,
}
