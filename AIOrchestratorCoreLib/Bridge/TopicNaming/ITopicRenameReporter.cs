namespace AIOrchestratorCoreLib.Bridge.TopicNaming;

/// <summary>
/// Decides which rename outcomes are NEWS for the log, so a successful rename is recorded without
/// recording the same answer again at every revalidation.
///
/// <para>
/// The name sync re-sends every topic's name every 5 minutes on purpose (the memo is a cache, not a
/// verdict), and Telegram answers most of those TOPIC_NOT_MODIFIED. Logging each would be a line per
/// topic per 5 minutes saying nothing; logging none is how dvfs-33 (2026-09-23) left no trace of
/// when its name landed. The rule: a line when the topic, the name or Telegram's answer differs
/// from the last one recorded for this orchestration.
/// </para>
/// </summary>
public interface ITopicRenameReporter
{
    /// <summary>
    /// Records the outcome and returns the line to log, or null when it repeats the last one for this
    /// orchestration.
    /// </summary>
    string? Record_OrNull(string orchId, long topicId, string name, TopicRenameAnswers answer);

    /// <summary>
    /// A topic was just CREATED with this name — Telegram's own confirmation, so an identical 200
    /// afterwards is not news. The creation has its own log line; this writes none.
    /// </summary>
    void Remember_Created(string orchId, long topicId, string name);

    /// <summary>The last outcome recorded for this orchestration, or null — `/refresh` quotes it.</summary>
    (long TopicId, string Name, TopicRenameAnswers Answer)? Last_OrNull(string orchId);
}
