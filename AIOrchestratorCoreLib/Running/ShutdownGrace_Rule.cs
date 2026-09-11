using AIOrchestratorCoreLib.Running.ClosingTurn;

namespace AIOrchestratorCoreLib.Running;

/// <summary>
/// HOW LONG A STOP MAY TAKE — one rule, three consumers, so they cannot disagree. The dispatcher
/// drains in-flight turns for the turn timeout plus the closing turn plus a minute; the host service
/// waits for the engine at least that long; and the .NET host itself must not cut StopAsync before
/// either has finished.
///
/// <para>
/// MEASURED 2026-09-09 17:24–17:30 on the VPS, three restarts: every time the daemon logged
/// "draining 2 in-flight turn(s) (up to 31 min)" and systemd logged "Deactivated successfully" exactly
/// 30 seconds later — <c>HostOptions.ShutdownTimeout</c> at its default, which nobody had set. Five
/// fresh sessions were killed mid-turn (15.2 M tokens), the same turn restarted four times from zero,
/// and no "Drain complete" line was ever written. The drain the unit promised (TimeoutStopSec 35 min)
/// was cut by the process, not by systemd.
/// </para>
/// </summary>
public static class ShutdownGrace_Rule
{
    /// <summary>What the dispatcher adds after the last turn could legitimately end.</summary>
    public static readonly TimeSpan DRAIN_MARGIN = TimeSpan.FromMinutes(1);

    /// <summary>What the host service adds on top of the dispatcher's drain before it kills sessions.</summary>
    public static readonly TimeSpan ENGINE_MARGIN = TimeSpan.FromMinutes(1);

    /// <summary>What the .NET host adds on top of the engine's grace before it abandons StopAsync.</summary>
    public static readonly TimeSpan HOST_MARGIN = TimeSpan.FromMinutes(2);

    /// <summary>The turn timeout the host is sized for when it cannot read the configuration (Program.cs runs before the config provider exists).</summary>
    public static readonly TimeSpan DEFAULT_TURN_TIMEOUT = TimeSpan.FromMinutes(30);

    /// <summary>The dispatcher's drain: a turn at its deadline, its closing turn, a minute.</summary>
    public static TimeSpan Compute_DrainGrace(TimeSpan turnTimeout)
    {
        return turnTimeout + ClosingTurn_Rule.Resolve_Timeout(turnTimeout) + DRAIN_MARGIN;
    }

    /// <summary>The host service's wait for the engine: the drain plus a minute.</summary>
    public static TimeSpan Compute_EngineStopGrace(TimeSpan turnTimeout)
    {
        return Compute_DrainGrace(turnTimeout) + ENGINE_MARGIN;
    }

    /// <summary>What <c>HostOptions.ShutdownTimeout</c> must be for the two above to ever finish.</summary>
    public static TimeSpan Compute_HostShutdownTimeout(TimeSpan turnTimeout)
    {
        return Compute_EngineStopGrace(turnTimeout) + HOST_MARGIN;
    }

    /// <summary>The value Program.cs installs: sized for the default turn timeout. A longer configured timeout is reported at startup (<see cref="Describe_Mismatch_OrNull"/>).</summary>
    public static TimeSpan HOST_SHUTDOWN_TIMEOUT => Compute_HostShutdownTimeout(DEFAULT_TURN_TIMEOUT);

    /// <summary>
    /// Null when the installed host timeout covers the configured turn timeout; otherwise one line
    /// saying by how much the drain will be cut — logged at startup, where an operator reads it, and
    /// never thrown: a daemon that refuses to start over a config value takes every session down.
    /// </summary>
    public static string? Describe_Mismatch_OrNull(TimeSpan configuredTurnTimeout, TimeSpan installedHostTimeout)
    {
        var needed = Compute_HostShutdownTimeout(configuredTurnTimeout);

        if (needed <= installedHostTimeout)
            return null;

        return $"the configured turn timeout ({configuredTurnTimeout.TotalMinutes:0} min) needs a host shutdown timeout of {needed.TotalMinutes:0} min to drain, but the host is installed with {installedHostTimeout.TotalMinutes:0} min — a stop during a long turn will cut the drain. Lower printRunner.turnTimeoutMinutes or raise ShutdownGrace_Rule.DEFAULT_TURN_TIMEOUT (and the unit's TimeoutStopSec above it).";
    }
}
