using AIOrchestratorCoreLib.Channels.DiscoveredChannel;
using AIOrchestratorCoreLib.Tailing.TailerPollResult;

namespace AIOrchestratorCoreLib.Tailing.ChannelTailer;

/// <summary>
/// Byte-offset tailer over channel files. On the FIRST sighting of a file its offset is set to the
/// current length (history is never mirrored). On later polls new bytes accumulate per file, and
/// entries are emitted only when COMPLETE: a following header proves completion immediately; the
/// trailing entry is emitted after the file has been quiet for the configured number of polls.
/// </summary>
public interface IChannelTailer
{
    ITailerPollResult Poll(IReadOnlyList<IDiscoveredChannel> channels);

    /// <summary>Current offsets snapshot, persisted by the bridge so restarts do not re-mirror.</summary>
    IReadOnlyDictionary<string, long> Get_OffsetsSnapshot();

    /// <summary>
    /// Re-anchors a file's offset after the bridge itself rewrote it (channel compaction). Without
    /// this the shrink would look like the append-only protocol breaking and the whole remaining
    /// file would be re-mirrored to Telegram.
    /// </summary>
    void Set_Offset(string channelFilePath, long offset);
}
