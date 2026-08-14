using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Tailing.ChannelTailer;

namespace AIOrchestratorCoreLib.Tailing;

/// <summary>
/// Compaction of ONE channel, guards included. Rewriting a channel moves the tailer's cursor, so the
/// decision and the re-anchoring belong together: every caller that archives a channel must ask the
/// same two questions first, in the same order.
/// <para>
/// It lives here, outside <c>BridgeEngineModel</c>, so the decision can be exercised directly rather
/// than through a hand-written replica of this sequence — a green that certified a copy of the code
/// and not the code (found by rev-3, 2026-08-13). What remains in the engine is the discovery loop
/// and its log line.
/// <para>
/// THE ENGINE IS NOT UNREACHABLE FROM A TEST, and this docstring said it was. `BridgeEngine_Factory.Create`
/// is public and five test files were already driving the engine when that sentence was written;
/// `ChannelCompactionLoopProbeTests` now covers the discovery loop the same way. The claim was
/// true-shaped and narrow to begin with — nothing covered `Compact_LongChannels` — and was carried
/// outward as a fact about the type until it read as an instruction not to try. Extraction is still
/// right, for the replica reason above; it was never right for this one.
/// </para>
/// </para>
/// <para>
/// WHAT THESE GUARDS DO NOT COVER, so nobody reads them as more than they are. Two windows, both
/// narrow, neither closed here, and they are NOT the same age:
/// (1) an entry appended between the guard's answer and the compactor's read is missed by the GUARD
/// and present in the COMPACTOR's read — so the rewrite keeps it and the re-anchor parks the cursor
/// past it, unmirrored. This one is a residual of the unread-bytes clause rather than something older
/// than it: that clause answers for the instant it was asked, which is microseconds before the read
/// instead of a whole tick phase before it. Narrower, same shape;
/// (2) an entry appended between the compactor's read and its rename-over is discarded from the FILE
/// and not merely from the mirror, because the rewrite replaces the file that append landed in. This
/// one predates everything here and lives in Channel_Compactor.
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

        // A guard that could not evaluate its predicate SAYS WHICH ONE. Silence in either direction
        // is the failure: an unexplained refusal is unactionable, and an invented "all clear" is how
        // a rewrite proceeds on a question nobody managed to ask. The log is the right home for it —
        // the owner cannot act on this, so it never goes to Telegram.
        //
        // It reports and FALLS THROUGH rather than returning here. An earlier version returned early,
        // which read as the refusal but was not: the predicate already answers TRUE when it cannot
        // evaluate, so that branch could never change the outcome and no mutation could kill it. The
        // refusal lives in the predicate; this says why.
        if (unevaluableReason != null)
        {
            log.Log_Warning(
                orchId,
                $"Compaction held off — the undelivered-entries guard could not evaluate '{Describe_Channel(channelFilePath)}': {unevaluableReason}");
        }

        if (owesDelivery)
            return null;

        var newLength = Channel_Compactor.Compact_IfNeeded(channelFilePath);

        if (newLength == null)
            return null;

        // Without this the next poll sees a file SHORTER than its cursor, which is the append-only
        // protocol breaking as far as the tailer knows: it reports the anomaly, resets the cursor to
        // the new length and drops whatever was pending. Nothing is re-mirrored — the truncation
        // branch returns no entries, so the old claim that "the whole remaining file is re-mirrored"
        // described a consequence this code cannot produce (rev-4, 2026-08-13).
        tailer.Set_Offset(channelFilePath, newLength.Value);

        return newLength;
    }

    /// <summary>
    /// Enough of the path to identify WHICH channel. Every member's file is called `channel.md`, so
    /// the file name alone names all of them equally — a log line that says "could not evaluate
    /// 'channel.md'" in a six-member orchestration has told the reader nothing they can act on.
    /// </summary>
    static string Describe_Channel(string channelFilePath)
    {
        var folder = Path.GetFileName(Path.GetDirectoryName(channelFilePath));

        return string.IsNullOrEmpty(folder)
            ? Path.GetFileName(channelFilePath)
            : $"{folder}/{Path.GetFileName(channelFilePath)}";
    }
}
