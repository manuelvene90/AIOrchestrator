namespace AIOrchestratorCoreLib.Channels.DiscoveredChannel;

public static class DiscoveredChannel_Factory
{
    public static IDiscoveredChannel Create_ForImplementer(string orchId, string memberId, string filePath)
    {
        return new DiscoveredChannelModel(orchId, memberId, filePath, isOwnerChannel: false);
    }

    public static IDiscoveredChannel Create_ForOwner(string orchId, string filePath)
    {
        return new DiscoveredChannelModel(orchId, "owner", filePath, isOwnerChannel: true);
    }
}
