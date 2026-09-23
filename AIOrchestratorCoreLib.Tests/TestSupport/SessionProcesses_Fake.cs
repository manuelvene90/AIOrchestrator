using AIOrchestratorCoreLib.Termination.SessionProcesses;

namespace AIOrchestratorCoreLib.Tests.TestSupport;

/// <summary>
/// The process table, faked: a pid file is "alive" only when a test says so, and a kill is recorded
/// instead of performed — no test ever stops a real process. Killing makes the slot dead, which is
/// what the real terminator does by deleting the pid file.
/// </summary>
internal sealed class SessionProcesses_Fake : ISessionProcesses
{
    readonly object _lock = new();
    readonly HashSet<string> _alive = [];
    readonly Dictionary<string, DateTime> _startedUtc = [];
    readonly List<string> _killed = [];

    /// <summary>Every slot reads as alive — for engine tests that seed transcripts but no pid files.</summary>
    public bool EverythingAlive { get; init; }

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
            return EverythingAlive ? !_killed.Contains(pidFilePath) : _alive.Contains(pidFilePath);
    }

    public void Kill_Tree(string pidFilePath)
    {
        lock (_lock)
        {
            _killed.Add(pidFilePath);
            _alive.Remove(pidFilePath);
        }
    }
}
