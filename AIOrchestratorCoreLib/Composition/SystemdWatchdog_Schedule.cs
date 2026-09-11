namespace AIOrchestratorCoreLib.Composition;

/// <summary>
/// How often the daemon must tell systemd it is alive. systemd hands the unit's WatchdogSec to
/// the process as WATCHDOG_USEC; a ping missed for that long restarts the service. Pinging at
/// HALF the interval is the sd_watchdog_enabled(3) recommendation: one late tick is absorbed, two
/// in a row are not. No variable, or one that does not parse, means no watchdog — never a guess.
/// </summary>
public static class SystemdWatchdog_Schedule
{
    public const string WATCHDOG_USEC_ENV = "WATCHDOG_USEC";

    public static TimeSpan? Compute_PingInterval_OrNull(string? watchdogMicroseconds)
    {
        if (!long.TryParse(watchdogMicroseconds, out var microseconds) || microseconds <= 0)
            return null;

        return TimeSpan.FromMicroseconds(microseconds / 2.0);
    }
}
