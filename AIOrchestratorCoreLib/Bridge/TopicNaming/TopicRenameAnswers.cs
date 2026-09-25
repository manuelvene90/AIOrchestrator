namespace AIOrchestratorCoreLib.Bridge.TopicNaming;

/// <summary>
/// The two answers Telegram gives to a rename that APPLIED — kept apart here although
/// <see cref="Telegram.TelegramAttempt_Gate"/> rightly folds them into one "applied" for retry
/// purposes. For the log they are different facts: one says Telegram just took the edit, the other
/// that it already had the name. Until 2026-09-23 neither was logged, so nobody could tell from the
/// files when a name landed (dvfs-33).
/// </summary>
public enum TopicRenameAnswers
{
    /// <summary>`editForumTopic` answered 200: the name changed now.</summary>
    Renamed,

    /// <summary>`editForumTopic` answered 400 TOPIC_NOT_MODIFIED: the topic already carried the name.</summary>
    AlreadyNamed,
}
