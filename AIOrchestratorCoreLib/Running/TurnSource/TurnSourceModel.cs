namespace AIOrchestratorCoreLib.Running.TurnSource;

internal sealed class TurnSourceModel(string key, string channelFilePath, TurnSourceKinds kind) : ITurnSource
{
    public string Key { get; } = key;
    public string ChannelFilePath { get; } = channelFilePath;
    public TurnSourceKinds Kind { get; } = kind;
    public bool IsOwnerChannel => Kind == TurnSourceKinds.Owner;
}
