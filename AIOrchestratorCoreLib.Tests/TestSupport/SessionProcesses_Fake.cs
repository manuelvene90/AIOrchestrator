using AIOrchestratorCoreLib.Termination.SessionProcesses;

namespace AIOrchestratorCoreLib.Tests.TestSupport;

/// <summary>
/// The process table, faked: a pid file is "alive" only when a test says so, and a kill is recorded
/// instead of performed — no test ever stops a real process. Killing makes the slot dead, which is
/// what the real terminator does by deleting the pid file; <see cref="Mark_Alive"/> brings it back,
/// which is what the watchdog's respawn does.
///
/// <para>
/// A TEST OF THE HALF-HOUR CAP MUST REVIVE THE SLOT (review of 171a23d, decision 20). A killed slot
/// reads as dead, and the rescue skips a dead slot before it ever consults the cap — so a cap test that
/// leaves the slot dead stays green with the cap deleted. Revive it with a start time BEFORE the
/// refusal, so the cap is the only brake left.
/// </para>
/// </summary>
internal sealed class SessionProcesses_Fake : ISessionProcesses
{
    readonly object _lock = new();
    readonly HashSet<string> _alive = [];
    readonly HashSet<string> _dead = [];
    readonly Dictionary<string, DateTime> _startedUtc = [];
    readonly List<string> _killed = [];

    /// <summary>Every slot reads as alive unless killed — for engine tests that seed transcripts but no pid files.</summary>
    public bool EverythingAlive { get; init; }

    /// <summary>Every kill, in order — the history, which a revive does not erase.</summary>
    public List<string> Killed
    {
        get
        {
            lock (_lock)
                return [.. _killed];
        }
    }

    public void Mark_Alive(string pidFilePath, DateTime? startedUtc = null)
    {
        lock (_lock)
        {
            _alive.Add(pidFilePath);
            _dead.Remove(pidFilePath);

            if (startedUtc != null)
                _startedUtc[pidFilePath] = startedUtc.Value;
        }
    }

    public DateTime? Read_StartedUtc_OrNull(string pidFilePath)
    {
        lock (_lock)
            return _startedUtc.TryGetValue(pidFilePath, out var startedUtc) ? startedUtc : null;
    }

    public bool Is_Alive(string pidFilePath)
    {
        lock (_lock)
            return EverythingAlive ? !_dead.Contains(pidFilePath) : _alive.Contains(pidFilePath);
    }

    public void Kill_Tree(string pidFilePath)
    {
        lock (_lock)
        {
            _killed.Add(pidFilePath);
            _alive.Remove(pidFilePath);
            _dead.Add(pidFilePath);
        }
    }
}
