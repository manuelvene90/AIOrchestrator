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
    /// Whether this file owes a delivery AS FAR AS THIS TAILER CAN ESTABLISH, from the three places
    /// one can be owed from: entries emitted but not yet acknowledged, bytes read but not yet
    /// emitted (only these survive a poll's rewind), and bytes on disk PAST THE CURSOR that no poll
    /// has read yet. The bridge must not rewrite such a file meanwhile.
    /// <para>
    /// It is a POINT-IN-TIME answer and not a lock, which is the honest limit of it: an entry
    /// appended after this returns — while the caller is still deciding, or while the compactor is
    /// reading the file — is outside what it was asked. Closing that needs the write held off for
    /// the whole decision, which nothing here does.
    /// </para>
    /// <para>
    /// The third is the one that is easy to forget: the mirror tick appends entries after its own
    /// poll, so a channel can owe a delivery that exists nowhere in this object's memory. It
    /// therefore stats the file rather than trusting its buffers.
    /// </para>
    /// <para>
    /// When it CANNOT tell — the file cannot be stat'ed, or has vanished under the cursor — it
    /// returns true and sets <paramref name="unevaluableReason"/> to the predicate that failed. The
    /// caller must log that line and hold off rather than let a rewrite proceed on an unasked
    /// question. A null reason means the answer was actually computed.
    /// </para>
    /// </summary>
    bool Has_UndeliveredEntries(string channelFilePath, out string? unevaluableReason);

    /// <summary>
    /// Whether this file was among the channels handed to the LAST <see cref="Poll"/>. A file the
    /// poll skipped has a frozen cursor — a deferred topic, an owner channel held mid-composition —
    /// and everything it produced is owed to the owner as a catch-up burst, so compaction asks this
    /// before rewriting anything. It reports whether the file was POLLED, which is not the same as
    /// whether its cursor is current — a polled file can still have grown since, which is
    /// <see cref="Has_UndeliveredEntries"/>'s third clause and not this one's business.
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
