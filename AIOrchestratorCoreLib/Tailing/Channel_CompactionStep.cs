using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
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
/// <para>
/// WHAT THESE GUARDS DO NOT COVER, so nobody reads them as more than they are. Both are pre-existing,
/// both are narrow, and neither is closed here:
/// (1) an entry appended between the guard's answer and the compactor's read is seen by neither, so
/// the rewrite keeps it and the re-anchor parks the cursor past it — the same loss the unread-bytes
/// clause closes, in a window of microseconds rather than a whole tick phase;
/// (2) an entry appended between the compactor's read and its rename-over is discarded from the FILE
/// and not merely from the mirror, because the rewrite replaces the file that append landed in.
/// Closing either means holding channel writes off across the whole read-decide-rewrite, which
/// nothing here does.
/// </para>
/// </summary>
public static class Channel_CompactionStep
{
    /// <summary>
    /// Archives the older entries of this channel and re-anchors the tailer to the rewritten file.
    /// Returns the new file length, or null when nothing was done — the channel was not eligible,
    /// or it was too short to compact.
    /// </summary>
    public static long? Compact_IfAllowed(
        IChannelTailer tailer,
        string channelFilePath,
        IOrchestrationLog log,
        string orchId)
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
        var owesDelivery = tailer.Has_UndeliveredEntries(channelFilePath, out var unevaluableReason);

        // A guard that could not evaluate its predicate SAYS WHICH ONE and holds off. Silence in
        // either direction is the failure: an unexplained refusal is unactionable, and an invented
        // "all clear" is how a rewrite proceeds on a question nobody managed to ask. The log is the
        // right home for it — the owner cannot act on this, so it never goes to Telegram.
        if (unevaluableReason != null)
        {
            log.Log_Warning(
                orchId,
                $"Compaction held off — the undelivered-entries guard could not evaluate '{Path.GetFileName(channelFilePath)}': {unevaluableReason}");

            return null;
        }

        if (owesDelivery)
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
