using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Tailing.ChannelTailer;

namespace AIOrchestratorCoreLib.Tailing;

/// <summary>
/// Compaction of ONE channel, guards included. Rewriting a channel moves the tailer's cursor, so the
/// decision and the re-anchoring belong together: every caller that archives a channel must ask the
/// same two questions first, in the same order.
/// <para>
/// It lives here, outside <c>BridgeEngineModel</c>, so the suite can execute the real decision. The
/// engine is internal, sealed, eleven dependencies deep and unreachable from a test, so the guards
/// used to be verified through a hand-written replica of this sequence — a green that certified a
/// copy of the code and not the code (found by rev-3, 2026-08-13). What remains in the engine is the
/// discovery loop and its log line.
/// </para>
/// </summary>
public static class Channel_CompactionStep
{
    /// <summary>
    /// Archives the older entries of this channel and re-anchors the tailer to the rewritten file.
    /// Returns the new file length, or null when nothing was done — the channel was not eligible,
    /// or it was too short to compact.
    /// </summary>
    public static long? Compact_IfAllowed(IChannelTailer tailer, string channelFilePath)
    {
        // A channel the poll SKIPPED has a frozen cursor — Find_ActiveChannels drops deferred topics
        // and held owner channels precisely so their offsets freeze and everything they produced
        // replays as a catch-up burst. Discovery is wider than the poll, so without this the frozen
        // cursor was re-anchored to EOF and the burst the owner is promised in writing arrived empty.
        if (!tailer.Was_PolledInLastPoll(channelFilePath))
            return null;

        // A channel that still owes Telegram a delivery must not be rewritten underneath the tailer:
        // compaction re-anchors the offset to the new file, and the entries waiting to be sent would
        // go with it. It compacts on a later tick, once the send lands.
        if (tailer.Has_UndeliveredEntries(channelFilePath))
            return null;

        var newLength = Channel_Compactor.Compact_IfNeeded(channelFilePath);

        if (newLength == null)
            return null;

        // Without this the shrink reads as the append-only protocol breaking and the whole remaining
        // file is re-mirrored to Telegram.
        tailer.Set_Offset(channelFilePath, newLength.Value);

        return newLength;
    }
}
