namespace AIOrchestratorCoreLib.Bridge.TopicNaming;

/// <summary>The log wording for a rename outcome — one place, so the tests and the engine read the same words.</summary>
public static class TopicRenameLine_Builder
{
    public static string Describe_Outcome(long topicId, string name, TopicRenameAnswers answer)
    {
        return answer switch
        {
            TopicRenameAnswers.Renamed => $"renamed topic {topicId} to '{name}'",
            TopicRenameAnswers.AlreadyNamed => $"topic {topicId} already named '{name}'",
            _ => throw new ArgumentOutOfRangeException(nameof(answer), answer, $"Unhandled TopicRenameAnswers: {answer}"),
        };
    }

    /// <summary>
    /// The same fact as the tail of the `/refresh` line — worded so it can never match either line
    /// above, which keeps "one line per distinct outcome" countable in the log.
    /// </summary>
    public static string Describe_ForRefresh(TopicRenameAnswers answer)
    {
        return answer switch
        {
            TopicRenameAnswers.Renamed => "Telegram applied the edit",
            TopicRenameAnswers.AlreadyNamed => "Telegram already had that name",
            _ => throw new ArgumentOutOfRangeException(nameof(answer), answer, $"Unhandled TopicRenameAnswers: {answer}"),
        };
    }
}
