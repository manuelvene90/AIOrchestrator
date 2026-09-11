namespace AIOrchestratorCoreLib.Bridge.EngineState;

/// <summary>
/// Holds the snapshot in memory. Not a stub: it is what makes a restart testable without a
/// filesystem, by outliving the engine that wrote it.
/// </summary>
internal sealed class MemoryEngineStateStoreModel : IEngineStateStore
{
    readonly object _lock = new();
    EngineStateSnapshot _snapshot = EngineStateSnapshot.Empty;

    public EngineStateSnapshot Load_OrEmpty()
    {
        lock (_lock)
            return _snapshot;
    }

    public void Save(EngineStateSnapshot snapshot)
    {
        lock (_lock)
            _snapshot = snapshot;
    }
}
