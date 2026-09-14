using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Tests.Kit;
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
    public void TheThreeWaits_AreStrictlyNested_ForTheLongestDefaultTurn()
    {
        var turn = ShutdownGrace_Rule.DEFAULT_LONGEST_TURN_TIMEOUT;

        var drain = ShutdownGrace_Rule.Compute_DrainGrace(turn);
        var engine = ShutdownGrace_Rule.Compute_EngineStopGrace(turn);
        var host = ShutdownGrace_Rule.Compute_HostShutdownTimeout(turn);

        Assert.True(turn < drain, "the drain must outlast the turn it waits for");
        Assert.True(drain < engine, "the host service must outlast the dispatcher's drain");
        Assert.True(engine < host, "the .NET host must outlast the host service");
        Assert.Equal(host, ShutdownGrace_Rule.HOST_SHUTDOWN_TIMEOUT);
    }

    /// <summary>
    /// THE HOST IS SIZED FOR THE LONGEST TURN, NOT THE COMMON ONE — a braked member's two hours since
    /// 2026-09-11. Sized for thirty minutes, a stop during a long member turn would cut its drain.
    /// </summary>
    [Fact]
    public void TheHostTimeout_CoversTheLongestDefaultTurn_WhichIsTheMemberCeiling()
    {
        Assert.Equal(AIOrchestratorCoreLib.Running.RunnerConfigs.RunnerConfigs_Factory.DEFAULT_MEMBER_TURN_TIMEOUT, ShutdownGrace_Rule.DEFAULT_LONGEST_TURN_TIMEOUT);
        Assert.Null(ShutdownGrace_Rule.Describe_Mismatch_OrNull(ShutdownGrace_Rule.DEFAULT_LONGEST_TURN_TIMEOUT, ShutdownGrace_Rule.HOST_SHUTDOWN_TIMEOUT));
    }

    /// <summary>
    /// SYSTEMD MUST SIGKILL LAST, read from the unit itself rather than from a number copied into this
    /// file — the copy said 2400 while the rule it guarded moved, which is the second-copy failure
    /// CLAUDE.md decision 12 names.
    /// </summary>
    [Fact]
    public void TheSystemdUnit_OutlastsTheHostTimeout()
    {
        var unit = KitRepoFiles.Find(Path.Combine("deploy", "systemd", "aiorchestrator.service"));
        Assert.NotNull(unit);

        var line = File.ReadAllLines(unit!).Single(text => text.StartsWith("TimeoutStopSec=", StringComparison.Ordinal));
        var unitTimeout = TimeSpan.FromSeconds(int.Parse(line["TimeoutStopSec=".Length..]));

        Assert.True(ShutdownGrace_Rule.HOST_SHUTDOWN_TIMEOUT < unitTimeout, $"host timeout {ShutdownGrace_Rule.HOST_SHUTDOWN_TIMEOUT} is not below the unit's {unitTimeout}");
    }

    [Fact]
    public void AConfiguredTurnTimeout_LongerThanTheHostIsSizedFor_IsReportedNotThrown()
    {
        Assert.Null(ShutdownGrace_Rule.Describe_Mismatch_OrNull(TimeSpan.FromMinutes(30), ShutdownGrace_Rule.HOST_SHUTDOWN_TIMEOUT));
        Assert.Null(ShutdownGrace_Rule.Describe_Mismatch_OrNull(TimeSpan.FromMinutes(10), ShutdownGrace_Rule.HOST_SHUTDOWN_TIMEOUT));

        Assert.Null(ShutdownGrace_Rule.Describe_Mismatch_OrNull(TimeSpan.FromMinutes(120), ShutdownGrace_Rule.HOST_SHUTDOWN_TIMEOUT));

        var warning = ShutdownGrace_Rule.Describe_Mismatch_OrNull(TimeSpan.FromMinutes(180), ShutdownGrace_Rule.HOST_SHUTDOWN_TIMEOUT);
        Assert.NotNull(warning);
        Assert.Contains("180 min", warning);
        Assert.Contains("will cut the drain", warning);
    }
}
