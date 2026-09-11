namespace AIOrchestratorCoreLib.Running.TurnSource;

internal sealed class TurnSourceModel(string key, string channelFilePath, bool isOwnerChannel) : ITurnSource
{
    public string Key { get; } = key;
    public string ChannelFilePath { get; } = channelFilePath;
    public bool IsOwnerChannel { get; } = isOwnerChannel;
}
