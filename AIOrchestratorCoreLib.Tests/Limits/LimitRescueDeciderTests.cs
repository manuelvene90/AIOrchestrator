using AIOrchestratorCoreLib.Limits;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Limits;

/// <summary>
/// When a terminal session still stuck on a usage limit is restarted — see
/// <see cref="LimitRescue_Decider"/> for the overnight incident this answers.
/// </summary>
public class LimitRescueDeciderTests
{
    static readonly DateTime Now = new(2026, 9, 23, 3, 0, 0, DateTimeKind.Utc);
    static readonly DateTime LongOpen = Now.AddHours(-1);
    static readonly DateTime LongRefused = Now.AddHours(-3);

    [Fact]
    public void AStuckSession_WithTheDispatcherLongOpen_IsRestarted()
    {
        Assert.True(LimitRescue_Decider.Should_Restart(true, LongRefused, LongOpen, null, Now));
    }

    [Fact]
    public void ASessionThatIsNotBlocked_IsNeverRestarted()
    {
        Assert.False(LimitRescue_Decider.Should_Restart(false, LongRefused, LongOpen, null, Now));
    }

    /// <summary>No refusal time is "cannot tell", and cannot-tell never kills a session.</summary>
    [Fact]
    public void AnUnknownRefusalTime_IsNeverRestarted()
    {
        Assert.False(LimitRescue_Decider.Should_Restart(true, null, LongOpen, null, Now));
    }

    /// <summary>
    /// Inside the grace after the window reopened, the CLI's own auto-continue gets its chance first —
    /// it fires at the reset instant, and killing it then would throw away a turn already restarting.
    /// </summary>
    [Fact]
    public void JustAfterTheDispatcherReopened_ItWaitsForTheGrace()
    {
        var justOpened = Now.AddMinutes(-(LimitRescue_Decider.GRACE_MINUTES - 1));

        Assert.False(LimitRescue_Decider.Should_Restart(true, LongRefused, justOpened, null, Now));
        Assert.True(LimitRescue_Decider.Should_Restart(true, LongRefused, Now.AddMinutes(-LimitRescue_Decider.GRACE_MINUTES), null, Now));
    }

    /// <summary>A refusal from a moment ago is an allowance nobody has measured yet — not a stuck session.</summary>
    [Fact]
    public void AFreshRefusal_IsNotRestarted()
    {
        var justRefused = Now.AddMinutes(-(LimitRescue_Decider.GRACE_MINUTES - 1));

        Assert.False(LimitRescue_Decider.Should_Restart(true, justRefused, LongOpen, null, Now));
    }

    /// <summary>
    /// One restart per half hour per session: the respawned session shows the OLD refusal until its
    /// new process writes a status line, and a session refused again must not be killed every tick.
    /// </summary>
    [Fact]
    public void ARecentlyRestartedSession_IsNotRestartedAgainInsideTheInterval()
    {
        var restarted = Now.AddMinutes(-(LimitRescue_Decider.MIN_INTERVAL_MINUTES - 1));
        var restartedLongAgo = Now.AddMinutes(-LimitRescue_Decider.MIN_INTERVAL_MINUTES);

        Assert.False(LimitRescue_Decider.Should_Restart(true, LongRefused, LongOpen, restarted, Now));
        Assert.True(LimitRescue_Decider.Should_Restart(true, LongRefused, LongOpen, restartedLongAgo, Now));
    }
}
