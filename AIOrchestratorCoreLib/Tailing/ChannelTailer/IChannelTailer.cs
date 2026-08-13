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
    /// Whether anything this tailer has READ is still owed a delivery: entries emitted but not yet
    /// acknowledged, and bytes read but not yet emitted — both are owed to the owner, and only the
    /// second survives a poll's rewind. The bridge must not rewrite such a file meanwhile.
    /// <para>
    /// LIMIT, because the honest statement of it is worth more than a clean sentence: this answers
    /// only for what was read. Bytes appended to the file since the last poll are owed too and are
    /// invisible here, so a rewrite between a poll and this question can still strand them. Closing
    /// that needs the cursor compared against the file's length, which is a separate open defect.
    /// </para>
    /// </summary>
    bool Has_UndeliveredEntries(string channelFilePath);

    /// <summary>
    /// Whether this file was among the channels handed to the LAST <see cref="Poll"/>. A file the
    /// poll skipped has a frozen cursor — a deferred topic, an owner channel held mid-composition —
    /// and everything it produced is owed to the owner as a catch-up burst, so compaction asks this
    /// before rewriting anything. It reports whether the file was POLLED, which is not the same as
    /// whether its cursor is current: a polled file can still have grown since (see the limit on
    /// <see cref="Has_UndeliveredEntries"/>).
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
