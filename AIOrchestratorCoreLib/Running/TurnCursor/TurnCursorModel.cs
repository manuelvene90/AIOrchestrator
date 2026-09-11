namespace AIOrchestratorCoreLib.Running.TurnCursor;

internal sealed class TurnCursorModel(string sourceKey, string channelFilePath, int highWaterIndex, IReadOnlySet<string> delivered) : ITurnCursor
{
    public string SourceKey { get; } = sourceKey;
    public string ChannelFilePath { get; } = channelFilePath;
    public int HighWaterIndex { get; } = highWaterIndex;
    public IReadOnlySet<string> Delivered { get; } = delivered;
}
