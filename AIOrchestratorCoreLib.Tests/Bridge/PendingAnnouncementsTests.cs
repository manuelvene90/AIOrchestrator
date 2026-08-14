using AIOrchestratorCoreLib.Bridge.PendingAnnouncements;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE QUEUE THAT TURNS A LOST ANNOUNCEMENT INTO A LATE ONE.
/// <para>
/// Mode-transition announcements are the one class of channel write a return-value check cannot
/// save: they fire on the EDGE, and by the time the append runs the transition is already recorded
/// in the mode state, so there is no memo to withhold. A lost entry means the supervisor is never
/// told the owner went away and keeps asking them questions.
/// </para>
/// <para>
/// ORDER IS THE PROPERTY MOST OF THESE PIN, because getting it wrong is worse than the defect: two
/// instructions delivered in the wrong order tell the supervisor to behave as if the owner is
/// present when they are away. "Late" is recoverable; "reordered" is actively misleading.
/// </para>
/// </summary>
public class PendingAnnouncementsTests
{
    const string CHANNEL = @"C:\channels\owner-channel.md";
    const string OTHER_CHANNEL = @"C:\channels\another-channel.md";

    static readonly DateTime NOW = new(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AQueuedAnnouncementIsDeliveredOnALaterDrain()
    {
        var queue = PendingAnnouncements_Factory.Create();

        queue.Queue("orch-1", CHANNEL, "the owner went away", "body", NOW);

        var delivered = new List<string>();
        var count = queue.Drain(pending =>
        {
            delivered.Add(pending.Subject);
            return true;
        });

        Assert.Equal(1, count);
        Assert.Equal(["the owner went away"], delivered);
        Assert.Equal(0, queue.Count);
    }

    /// <summary>
    /// THE ORDERING RULE. A channel that fails mid-drain keeps everything behind the failure, in
    /// order — a drain that carried on would deliver "the owner is back" while "the owner went away"
    /// stayed queued, and the supervisor would act on the wrong one.
    /// </summary>
    [Fact]
    public void AFailureStopsThatChannelsDrain_SoNothingOvertakesWhatIsStuck()
    {
        var queue = PendingAnnouncements_Factory.Create();

        queue.Queue("orch-1", CHANNEL, "first", "body", NOW);
        queue.Queue("orch-1", CHANNEL, "second", "body", NOW.AddSeconds(1));
        queue.Queue("orch-1", CHANNEL, "third", "body", NOW.AddSeconds(2));

        var attempted = new List<string>();

        var delivered = queue.Drain(pending =>
        {
            attempted.Add(pending.Subject);
            return false;
        });

        Assert.Equal(0, delivered);

        // Attempted ONCE, not three times: the first failure ends the channel's pass.
        Assert.Equal(["first"], attempted);
        Assert.Equal(3, queue.Count);
    }

    [Fact]
    public void AfterTheLockClears_TheyArriveInTheOrderTheyWereQueued()
    {
        var queue = PendingAnnouncements_Factory.Create();

        queue.Queue("orch-1", CHANNEL, "away ON", "body", NOW);
        queue.Queue("orch-1", CHANNEL, "away OFF", "body", NOW.AddSeconds(1));

        var delivered = new List<string>();

        queue.Drain(pending =>
        {
            delivered.Add(pending.Subject);
            return true;
        });

        Assert.Equal(["away ON", "away OFF"], delivered);
    }

    /// <summary>
    /// One wedged channel must not hold up another's announcements — they have no ordering
    /// relationship, and a shared queue that stopped at the first failure anywhere would let a
    /// single dead channel silence the whole app.
    /// </summary>
    [Fact]
    public void AWedgedChannelDoesNotHoldUpADifferentChannel()
    {
        var queue = PendingAnnouncements_Factory.Create();

        queue.Queue("orch-1", CHANNEL, "stuck", "body", NOW);
        queue.Queue("orch-2", OTHER_CHANNEL, "fine", "body", NOW);

        var delivered = new List<string>();

        var count = queue.Drain(pending =>
        {
            if (pending.ChannelFile == CHANNEL)
                return false;

            delivered.Add(pending.Subject);
            return true;
        });

        Assert.Equal(1, count);
        Assert.Equal(["fine"], delivered);

        // The wedged one is still waiting and the healthy one is gone: exactly one left.
        Assert.Equal(1, queue.Count);

        var retried = new List<string>();

        queue.Drain(pending =>
        {
            retried.Add(pending.Subject);
            return true;
        });

        Assert.Equal(["stuck"], retried);
    }

    /// <summary>
    /// THE ORDERING PROPERTY THAT ONE-WRITER BUYS: a second announcement arriving for a channel while
    /// the first is still queued comes out BEHIND it, never in front.
    /// <para>
    /// This is the case the old <c>Has_Queued_For</c> guard was meant to serve and could not — an
    /// append still WAITING on the lock was in neither state, so a concurrent announcement saw an
    /// empty queue and overtook it. With <c>Announce</c> unable to write at all there is no such
    /// state: everything is queued, so everything is ordered.
    /// </para>
    /// </summary>
    [Fact]
    public void ASecondAnnouncementArrivingWhileTheFirstIsStuck_ComesOutBehindIt()
    {
        var queue = PendingAnnouncements_Factory.Create();

        queue.Queue("orch-1", CHANNEL, "the owner went away", "body", NOW);

        // The channel is locked, so the first does not land.
        Assert.Equal(0, queue.Drain(_ => false));

        // The owner texts, which is what ENDS away mode — the real sequence, on the other loop.
        queue.Queue("orch-1", CHANNEL, "the owner is back", "body", NOW.AddSeconds(3));

        var delivered = new List<string>();

        queue.Drain(pending =>
        {
            delivered.Add(pending.Subject);
            return true;
        });

        Assert.Equal(["the owner went away", "the owner is back"], delivered);
    }

    /// <summary>
    /// BOUNDED. A retry queue with no cap is a leak the moment a channel stays wedged. The OLDEST
    /// goes, because the newest state is the one worth keeping — and it is RETURNED so the caller
    /// can log it, since a silent drop is the failure this whole mechanism exists to end.
    /// </summary>
    [Fact]
    public void PastTheCap_TheOldestIsDroppedAndReturnedSoItCanBeLogged()
    {
        var queue = PendingAnnouncements_Factory.Create();

        for (var index = 0; index < IPendingAnnouncements.PER_CHANNEL_CAP; index++)
            Assert.Null(queue.Queue("orch-1", CHANNEL, $"announcement {index}", "body", NOW.AddSeconds(index)));

        var dropped = queue.Queue("orch-1", CHANNEL, "the newest", "body", NOW.AddMinutes(1));

        Assert.NotNull(dropped);
        Assert.Equal("announcement 0", dropped.Subject);
        Assert.Equal(IPendingAnnouncements.PER_CHANNEL_CAP, queue.Count);

        var delivered = new List<string>();

        queue.Drain(pending =>
        {
            delivered.Add(pending.Subject);
            return true;
        });

        Assert.Equal("announcement 1", delivered[0]);
        Assert.Equal("the newest", delivered[^1]);
    }
}
