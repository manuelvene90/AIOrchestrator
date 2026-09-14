using Xunit;
using AIOrchestratorCoreLib.Telegram.Outbound;

namespace AIOrchestratorCoreLib.Tests.Telegram.Outbound;

/// <summary>
/// WHAT IS WAITING TO BE SAID, and the two rules that govern it: a job keeps its place in line and
/// is never discarded; a slot keeps only its latest value, because painting a state that has
/// already been superseded is always waste.
/// </summary>
public class OutboundQueueTests
{
    static readonly DateTime NOW = new(2026, 9, 11, 9, 27, 0, DateTimeKind.Utc);

    static OutboundIntent Slot(string key, DateTime at, OutboundBands band = OutboundBands.Cosmetic) =>
        OutboundIntent.Slot(band, "chat:1", key, "msg:1", (_, _) => Task.FromResult<long?>(null), null, at);

    static OutboundIntent Job(string orderKey, DateTime at, OutboundBands band = OutboundBands.Conversation) =>
        OutboundIntent.Job(band, "chat:1", orderKey, "chat:1", (_, _) => Task.FromResult<long?>(null), null, at);

    [Fact]
    public void A_NEWER_SLOT_REPLACES_AN_OLDER_ONE_THAT_HAS_NOT_GONE_OUT()
    {
        // The coalescing that makes N topics cost at most N pending repaints instead of a backlog
        // that grows with the tick rate.
        var queue = new OutboundQueue();

        queue.Publish(Slot("statusline:fincanva-5", NOW));
        queue.Publish(Slot("statusline:fincanva-5", NOW.AddSeconds(2)));

        var waiting = queue.Snapshot();

        Assert.Single(waiting);
        Assert.Equal(NOW.AddSeconds(2), waiting[0].EnqueuedUtc);
        Assert.Equal(1, queue.Count_Dropped_BySupersession());
    }

    [Fact]
    public void TWO_JOBS_ON_ONE_CHANNEL_KEEP_THEIR_ORDER_AND_ONLY_THE_HEAD_IS_OFFERED()
    {
        // A message that retries must not be overtaken by the next one on the same channel, so the
        // store offers only the head — head-of-line blocking is a property of the store rather than
        // a rule the scheduler has to remember.
        var queue = new OutboundQueue();

        queue.Publish(Job("/channel/a.md", NOW));
        queue.Publish(Job("/channel/a.md", NOW.AddSeconds(1)));

        var waiting = queue.Snapshot();

        Assert.Single(waiting);
        Assert.Equal(NOW, waiting[0].EnqueuedUtc);
        Assert.Equal(2, queue.Count_Waiting());
    }

    [Fact]
    public void TWO_JOBS_ON_DIFFERENT_CHANNELS_DO_NOT_BLOCK_EACH_OTHER()
    {
        var queue = new OutboundQueue();

        queue.Publish(Job("/channel/a.md", NOW));
        queue.Publish(Job("/channel/b.md", NOW));

        Assert.Equal(2, queue.Snapshot().Count);
    }

    [Fact]
    public void A_JOB_IS_NEVER_DROPPED_BY_SUPERSESSION()
    {
        // The owner's ruling of 2026-09-10: only a superseded cosmetic repaint may be discarded. A
        // conversation message is never lost, even at the cost of delay.
        var queue = new OutboundQueue();

        queue.Publish(Job("/channel/a.md", NOW));
        queue.Publish(Job("/channel/a.md", NOW.AddSeconds(1)));

        Assert.Equal(0, queue.Count_Dropped_BySupersession());
    }

    [Fact]
    public void REMOVING_THE_HEAD_OFFERS_THE_NEXT_ONE()
    {
        var queue = new OutboundQueue();
        var first = Job("/channel/a.md", NOW);

        queue.Publish(first);
        queue.Publish(Job("/channel/a.md", NOW.AddSeconds(1)));

        Assert.True(queue.Remove(first));
        Assert.Equal(NOW.AddSeconds(1), queue.Snapshot()[0].EnqueuedUtc);
    }

    [Fact]
    public void REMOVING_A_SLOT_THAT_WAS_OVERWRITTEN_IN_FLIGHT_IS_NOT_AN_ERROR()
    {
        // The pump takes a slot, and while it is on the wire a newer value is published. Removing
        // the one it sent must not delete the newer one, and must not throw: it simply lost. Pinned
        // because the obvious implementation — comparing by value — would silently delete the newer
        // one, since two repaints of an unchanged surface are equal records.
        var queue = new OutboundQueue();
        var taken = Slot("statusline:fincanva-5", NOW);

        queue.Publish(taken);
        queue.Publish(Slot("statusline:fincanva-5", NOW.AddSeconds(2)));

        Assert.False(queue.Remove(taken));
        Assert.Single(queue.Snapshot());
    }
}
