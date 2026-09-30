namespace AIOrchestratorCoreLib.Telegram.TopicTraffic;

internal sealed class TopicTrafficModel : ITopicTraffic
{
    /// <summary>
    /// Concurrent writers are the norm, not the exception: the mirror tick, the inbound poll loop and
    /// the fire-and-forget sends all send through the one recording client.
    /// </summary>
    readonly object _lock = new();

    readonly Dictionary<long, long> _newestMessageIdByThread = [];
    readonly Dictionary<long, DateTime> _lastSessionMessageAtUtcByThread = [];

    public void Note_Message(long? messageThreadId, long messageId)
    {
        lock (_lock)
        {
            var key = messageThreadId ?? 0;

            // THE HIGHEST id wins, not the last one recorded: ids arrive from a batch of updates and
            // from concurrent sends, so "most recently handed to this method" is not "latest in the
            // chat". An out-of-order id overwriting a higher one would tell the status line it is no
            // longer buried when it still is.
            if (!_newestMessageIdByThread.TryGetValue(key, out var newest) || messageId > newest)
                _newestMessageIdByThread[key] = messageId;
        }
    }

    public void Note_UnidentifiedMessage(long? messageThreadId)
    {
        lock (_lock)
        {
            var key = messageThreadId ?? 0;

            // A LOWER BOUND STANDS IN FOR THE ID, and it is a sound one. Telegram message ids rise
            // within a chat, and this message was sent AFTER the newest one recorded here already
            // existed, so its real id is at least that one plus one. Recording the bound:
            //   - buries a line whose id is at or below the newest known — which, with the line's own
            //     post recorded, is the line whenever it was the last message;
            //   - never outruns reality: the line's repost, sent after this message, gets an id above
            //     the message's real one, hence at or above the bound — and EQUAL is not buried.
            // Nothing is recorded when nothing is known yet: the restart assumption in
            // Find_Newest_OrAssumeBuried already treats such a topic's line as buried.
            //
            // The bound never reaches the known-ids list /clear deletes from — that list is the
            // engine's, and a guessed id there would delete somebody else's message.
            if (_newestMessageIdByThread.TryGetValue(key, out var newest))
                _newestMessageIdByThread[key] = newest + 1;
        }
    }

    public void Note_SessionMessage(long? messageThreadId, DateTime atUtc)
    {
        lock (_lock)
        {
            var key = messageThreadId ?? 0;

            // The LATEST stamp wins, for the reason the highest id does above: two entries mirrored
            // out of order must not move the session's clock backwards and release the line early.
            if (!_lastSessionMessageAtUtcByThread.TryGetValue(key, out var last) || atUtc > last)
                _lastSessionMessageAtUtcByThread[key] = atUtc;
        }
    }

    public TopicStatusLine_Planner.TopicNewestMessage? Find_Newest_OrAssumeBuried(long? messageThreadId, long? statusLineMessageId)
    {
        lock (_lock)
        {
            var key = messageThreadId ?? 0;

            if (_newestMessageIdByThread.TryGetValue(key, out var newest))
                return new TopicStatusLine_Planner.TopicNewestMessage(newest);

            if (statusLineMessageId == null)
                return null;

            // RECORDED, not merely returned: the assumption is made once. From here the topic resolves
            // like any other — the repost's id is recorded above it, or later traffic raises it.
            var assumed = statusLineMessageId.Value + 1;
            _newestMessageIdByThread[key] = assumed;

            return new TopicStatusLine_Planner.TopicNewestMessage(assumed);
        }
    }

    public DateTime? Find_LastSessionMessageAtUtc_OrNull(long? messageThreadId)
    {
        lock (_lock)
        {
            return _lastSessionMessageAtUtcByThread.TryGetValue(messageThreadId ?? 0, out var last) ? last : null;
        }
    }
}
