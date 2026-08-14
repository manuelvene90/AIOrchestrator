namespace AIOrchestratorCoreLib.Bridge.PendingAnnouncements;

/// <summary>One queued announcement. Immutable: nothing may edit what is waiting to be written.</summary>
internal sealed record PendingAnnouncementModel(
    string OrchId,
    string ChannelFile,
    Channels.AppEntryAudiences Audience,
    string Subject,
    string Body,
    DateTime QueuedUtc) : IPendingAnnouncement;

/// <inheritdoc cref="IPendingAnnouncements"/>
internal sealed class PendingAnnouncementsModel : IPendingAnnouncements
{
    // Keyed by channel because ORDER is a per-channel property: two channels have no ordering
    // relationship with each other, and one wedged channel must not hold up another's retries.
    readonly Dictionary<string, List<IPendingAnnouncement>> _byChannel =
        new(StringComparer.OrdinalIgnoreCase);

    readonly Lock _gate = new();

    public int Count
    {
        get
        {
            lock (_gate)
                return _byChannel.Values.Sum(queued => queued.Count);
        }
    }

    public IPendingAnnouncement? Queue(string orchId, string channelFile, Channels.AppEntryAudiences audience, string subject, string body, DateTime nowUtc)
    {
        lock (_gate)
        {
            if (!_byChannel.TryGetValue(channelFile, out var queued))
            {
                queued = [];
                _byChannel[channelFile] = queued;
            }

            queued.Add(new PendingAnnouncementModel(orchId, channelFile, audience, subject, body, nowUtc));

            if (queued.Count <= IPendingAnnouncements.PER_CHANNEL_CAP)
                return null;

            // Drop the OLDEST: when a wedged channel forces a choice, the newest state is the one
            // worth keeping. Returned rather than logged here — this type has no logger, and a drop
            // that says nothing is the silence the queue exists to end.
            var dropped = queued[0];

            queued.RemoveAt(0);

            return dropped;
        }
    }

    public int Drain(Func<IPendingAnnouncement, bool> attempt)
    {
        var delivered = 0;

        // Snapshot the channel keys, then attempt OUTSIDE the lock: the callback performs a channel
        // write that takes the file lock and can wait, and holding this gate across it would let one
        // contended channel block every other caller of this queue.
        List<string> channels;

        lock (_gate)
            channels = [.. _byChannel.Keys];

        foreach (var channel in channels)
        {
            while (true)
            {
                IPendingAnnouncement? next;

                lock (_gate)
                {
                    if (!_byChannel.TryGetValue(channel, out var queued) || queued.Count == 0)
                        break;

                    next = queued[0];
                }

                if (!attempt(next))
                    break;

                lock (_gate)
                {
                    // Re-find rather than trusting the earlier lookup: Queue may have run in between
                    // and it is the same list object, but the entry must be removed by IDENTITY, not
                    // by index, or a concurrent drop of the oldest would remove the wrong one.
                    if (_byChannel.TryGetValue(channel, out var queued))
                        queued.Remove(next);
                }

                delivered++;
            }
        }

        return delivered;
    }
}
