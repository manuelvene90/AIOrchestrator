using AIOrchestratorCoreLib.Channels.DiscoveredChannel;
using AIOrchestratorCoreLib.Tailing.TailerPollResult;

namespace AIOrchestratorCoreLib.Tailing.ChannelTailer;

/// <summary>
/// Byte-offset tailer over channel files. On the FIRST sighting of a file its offset is set to the
/// current length (history is never mirrored). On later polls new bytes accumulate per file, and
/// entries are emitted only when COMPLETE: a following header proves completion immediately; the
/// trailing entry is emitted after the file has been quiet for the configured number of polls.
/// <para>
/// Emission is AT-LEAST-ONCE, by contract: entries stay emitted-but-unconfirmed until the caller
/// calls <see cref="Confirm_Append"/>, and every poll re-emits whatever is still unconfirmed. A
/// caller that never confirms therefore sees the same entries again — which is the point. Before
/// this, the offset advanced during the read and a failed Telegram send dropped the owner's
/// messages for good, with nothing but a log line to say so.
/// </para>
/// </summary>
public interface IChannelTailer
{
    ITailerPollResult Poll(IReadOnlyList<IDiscoveredChannel> channels);

    /// <summary>
    /// The entries last emitted for this file were delivered (or deliberately abandoned), so the
    /// persisted cursor may move past them and later polls must stop re-emitting them.
    /// </summary>
    void Confirm_Append(string channelFilePath);

    /// <summary>
    /// Whether this file still owes a delivery — the bridge must not rewrite it meanwhile. That
    /// covers entries emitted but not yet acknowledged AND bytes already read that have not been
    /// emitted yet: both are owed to the owner, and only the second survives a poll's rewind.
    /// </summary>
    bool Has_UndeliveredEntries(string channelFilePath);

    /// <summary>
    /// Whether this file was among the channels handed to the LAST <see cref="Poll"/>. A file the
    /// poll skipped has a frozen cursor — a deferred topic, an owner channel held mid-composition —
    /// and everything it produced is owed to the owner as a catch-up burst. Nothing may re-anchor a
    /// cursor that did not move: compaction must ask this first.
    /// </summary>
    bool Was_PolledInLastPoll(string channelFilePath);

    /// <summary>
    /// Current offsets snapshot, persisted by the bridge so restarts do not re-mirror. Unconfirmed
    /// entries are excluded, so a restart re-reads and re-sends what the previous process failed to.
    /// </summary>
    IReadOnlyDictionary<string, long> Get_OffsetsSnapshot();

    /// <summary>
    /// Re-anchors a file's offset after the bridge itself rewrote it (channel compaction). Without
    /// this the shrink would look like the append-only protocol breaking and the whole remaining
    /// file would be re-mirrored to Telegram.
    /// </summary>
    void Set_Offset(string channelFilePath, long offset);
}
