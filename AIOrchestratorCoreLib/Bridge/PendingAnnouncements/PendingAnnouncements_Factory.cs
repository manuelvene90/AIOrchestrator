namespace AIOrchestratorCoreLib.Bridge.PendingAnnouncements;

/// <summary>Builds the queue that holds announcements whose channel was locked.</summary>
public static class PendingAnnouncements_Factory
{
    public static IPendingAnnouncements Create()
    {
        return new PendingAnnouncementsModel();
    }
}
