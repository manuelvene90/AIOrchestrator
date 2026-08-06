namespace AIOrchestratorCoreLib.Channels.DiscoveredChannel;

internal sealed class DiscoveredChannelModel(
    string orchId,
    string spokeName,
    string filePath,
    bool isOwnerChannel) : IDiscoveredChannel
{
    public string OrchId { get; } = orchId;
    public string SpokeName { get; } = spokeName;
    public string FilePath { get; } = filePath;
    public bool IsOwnerChannel { get; } = isOwnerChannel;
}
