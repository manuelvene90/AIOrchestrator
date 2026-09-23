namespace AIOrchestratorCoreLib.Bridge.TopicNaming;

/// <summary>Builds the memo that keeps topic-rename log lines to one per distinct outcome.</summary>
public static class TopicRenameReporter_Factory
{
    public static ITopicRenameReporter Create()
    {
        return new TopicRenameReporterModel();
    }
}
