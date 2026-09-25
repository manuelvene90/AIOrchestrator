namespace AIOrchestratorCoreLib.Bridge.TopicNaming;

/// <summary>
/// In memory on purpose: after a restart the first answer per topic is logged once again, which is
/// the useful reading — it says what Telegram held when this process first asked.
/// </summary>
internal sealed class TopicRenameReporterModel : ITopicRenameReporter
{
    readonly Lock _lock = new();
    readonly Dictionary<string, (long TopicId, string Name, TopicRenameAnswers Answer)> _last = [];

    public string? Record_OrNull(string orchId, long topicId, string name, TopicRenameAnswers answer)
    {
        lock (_lock)
        {
            var outcome = (topicId, name, answer);

            if (_last.TryGetValue(orchId, out var previous) && previous == outcome)
                return null;

            _last[orchId] = outcome;

            return TopicRenameLine_Builder.Describe_Outcome(topicId, name, answer);
        }
    }

    public void Remember_Created(string orchId, long topicId, string name)
    {
        lock (_lock)
            _last[orchId] = (topicId, name, TopicRenameAnswers.Renamed);
    }

    public (long TopicId, string Name, TopicRenameAnswers Answer)? Last_OrNull(string orchId)
    {
        lock (_lock)
            return _last.TryGetValue(orchId, out var last) ? last : null;
    }
}
