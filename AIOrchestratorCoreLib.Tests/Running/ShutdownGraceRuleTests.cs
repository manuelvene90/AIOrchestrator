using AIOrchestratorCoreLib.Running;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// The 2026-09-09 restarts: the dispatcher promised a 31–36 minute drain and the .NET host cut it at
/// its default 30 s. These pin the ordering that makes the promise true — drain &lt; engine grace &lt;
/// host shutdown timeout &lt; the systemd unit — and the startup warning for a config that outgrows it.
/// </summary>
public class ShutdownGraceRuleTests
{
    [Fact]
    public void TheThreeWaits_AreStrictlyNested_ForTheDefaultTurnTimeout()
    {
        var turn = ShutdownGrace_Rule.DEFAULT_TURN_TIMEOUT;

        var drain = ShutdownGrace_Rule.Compute_DrainGrace(turn);
        var engine = ShutdownGrace_Rule.Compute_EngineStopGrace(turn);
        var host = ShutdownGrace_Rule.Compute_HostShutdownTimeout(turn);

        Assert.True(turn < drain, "the drain must outlast the turn it waits for");
        Assert.True(drain < engine, "the host service must outlast the dispatcher's drain");
        Assert.True(engine < host, "the .NET host must outlast the host service");
        Assert.Equal(host, ShutdownGrace_Rule.HOST_SHUTDOWN_TIMEOUT);
    }

    [Fact]
    public void TheInstalledHostTimeout_IsWellAboveTheDefault30Seconds_AndBelowTheSystemdUnit()
    {
        // deploy/systemd/aiorchestrator.service: TimeoutStopSec=2400 — systemd must SIGKILL last.
        Assert.True(ShutdownGrace_Rule.HOST_SHUTDOWN_TIMEOUT > TimeSpan.FromMinutes(35), $"host timeout is {ShutdownGrace_Rule.HOST_SHUTDOWN_TIMEOUT}");
        Assert.True(ShutdownGrace_Rule.HOST_SHUTDOWN_TIMEOUT < TimeSpan.FromSeconds(2400), $"host timeout is {ShutdownGrace_Rule.HOST_SHUTDOWN_TIMEOUT}");
    }

    [Fact]
    public void AConfiguredTurnTimeout_LongerThanTheHostIsSizedFor_IsReportedNotThrown()
    {
        Assert.Null(ShutdownGrace_Rule.Describe_Mismatch_OrNull(TimeSpan.FromMinutes(30), ShutdownGrace_Rule.HOST_SHUTDOWN_TIMEOUT));
        Assert.Null(ShutdownGrace_Rule.Describe_Mismatch_OrNull(TimeSpan.FromMinutes(10), ShutdownGrace_Rule.HOST_SHUTDOWN_TIMEOUT));

        var warning = ShutdownGrace_Rule.Describe_Mismatch_OrNull(TimeSpan.FromMinutes(60), ShutdownGrace_Rule.HOST_SHUTDOWN_TIMEOUT);
        Assert.NotNull(warning);
        Assert.Contains("60 min", warning);
        Assert.Contains("will cut the drain", warning);
    }
}
