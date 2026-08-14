namespace AIOrchestratorCoreLib.Bridge.PendingAnnouncements;

/// <summary>
/// One announcement that could not be written, waiting for a later tick.
/// </summary>
public interface IPendingAnnouncement
{
    /// <summary>Which orchestration it belongs to, for the log line if it is ever dropped.</summary>
    string OrchId { get; }

    /// <summary>The channel file it must be appended to. Ordering is per this value.</summary>
    string ChannelFile { get; }

    string Subject { get; }

    string Body { get; }

    /// <summary>When it was first queued — the age a diagnostic needs to be worth reading.</summary>
    DateTime QueuedUtc { get; }
}

/// <summary>
/// HOLDS ANNOUNCEMENTS WHOSE CHANNEL WAS LOCKED, SO A LOST ONE BECOMES A LATE ONE.
/// <para>
/// The mode-transition announcements are the one class of channel write a return-value check cannot
/// save. They fire on the EDGE: by the time the append runs, the transition is already recorded in
/// the mode state, so there is no memo to withhold — withholding one would mean refusing to change
/// the mode, which is not the appender's to refuse. A lost entry means the supervisor is never told
/// the owner went away and keeps asking them questions, which is exactly what away mode exists to
/// stop. The fix therefore has to be a mechanism that survives to the next tick, not a check.
/// </para>
/// <para>
/// ORDER IS THE HARD PART AND IT IS PER CHANNEL. "The owner is back" above "the owner went away"
/// tells the supervisor to behave as if the owner is present when they are away: reordered actively
/// misleads, whereas late merely delays. Order holds because EVERY announcement comes through here
/// and ONE writer drains it — a single writer over a per-channel FIFO cannot interleave with itself.
/// <see cref="Drain"/> also stops at the first failure per channel, so a still-locked channel cannot
/// let what is behind it overtake.
/// </para>
/// <para>
/// A <c>Has_Queued_For</c> check used to serve this, with the caller appending directly when nothing
/// was queued. It could not work: an append still WAITING on the channel lock is in neither state,
/// so a concurrent announcement saw an empty queue and overtook it. Removing the direct write removed
/// the race instead of guarding it.
/// </para>
/// <para>
/// BOUNDED ON PURPOSE. A retry queue with no cap is a leak the moment a channel stays wedged, so
/// each channel holds at most <see cref="PER_CHANNEL_CAP"/>. Overflow drops the OLDEST and says so:
/// when something has to go, the newest state is the one worth keeping.
/// </para>
/// </summary>
public interface IPendingAnnouncements
{
    /// <summary>How many announcements are waiting, across every channel.</summary>
    int Count { get; }

    /// <summary>
    /// Adds an announcement to the back of its channel's queue. Returns the announcement that was
    /// DROPPED to make room, or null when nothing was dropped — the caller logs it, because this
    /// type has no logger and a silent drop is the failure this whole mechanism exists to end.
    /// </summary>
    IPendingAnnouncement? Queue(string orchId, string channelFile, string subject, string body, DateTime nowUtc);

    /// <summary>
    /// Retries what is waiting, oldest first within each channel, and removes whatever lands.
    /// <para>
    /// <paramref name="attempt"/> returns whether the append succeeded. The FIRST failure on a
    /// channel ends that channel's drain for this pass — anything behind it stays queued and stays
    /// in order. Other channels continue regardless, so one wedged channel cannot hold up the rest.
    /// </para>
    /// <para>Returns how many were delivered.</para>
    /// </summary>
    int Drain(Func<IPendingAnnouncement, bool> attempt);

    /// <summary>The most a single channel may hold before the oldest is dropped.</summary>
    const int PER_CHANNEL_CAP = 20;
}
