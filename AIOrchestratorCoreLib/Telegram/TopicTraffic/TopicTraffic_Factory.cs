namespace AIOrchestratorCoreLib.Telegram.TopicTraffic;

public static class TopicTraffic_Factory
{
    /// <summary>An empty record — what every process starts with; see <see cref="ITopicTraffic.Find_Newest_OrAssumeBuried"/> for the restart.</summary>
    public static ITopicTraffic Create_Empty()
    {
        return new TopicTrafficModel();
    }
}
