using AIOrchestratorCoreLib.Composition;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Composition;

public class SystemdWatchdogScheduleTests
{
    [Fact]
    public void ThePingRuns_AtHalfTheUnitsWatchdogSec()
    {
        // WatchdogSec=60 arrives as 60 000 000 µs.
        Assert.Equal(TimeSpan.FromSeconds(30), SystemdWatchdog_Schedule.Compute_PingInterval_OrNull("60000000"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("sixty")]
    public void NoWatchdog_OrAnUnreadableOne_MeansNoPinging_NeverAGuessedInterval(string? value)
    {
        Assert.Null(SystemdWatchdog_Schedule.Compute_PingInterval_OrNull(value));
    }
}
