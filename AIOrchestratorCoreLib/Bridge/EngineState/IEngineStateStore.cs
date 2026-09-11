namespace AIOrchestratorCoreLib.Bridge.EngineState;

/// <summary>
/// Where the bridge's decision state lives across a restart.
///
/// <para>
/// TWO IMPLEMENTATIONS, AND THE SECOND ONE IS THE POINT. The file store is production; the in-memory
/// store lets a test hand the SAME store to two successive engines, which is how "kill the bridge
/// with five decisions pending and start it again" becomes an assertion instead of a hope. Without
/// the seam that scenario can only be written as a sleep and a filesystem, which is the shape of
/// test this repo already has too many of.
/// </para>
/// <para>
/// <b>Save never throws for the state of the disk.</b> A bridge that cannot persist must keep
/// running — everything it holds is still in memory and still correct for this process. The failure
/// is reported once through the log the store was built with; escalating it would turn a full disk
/// into a phone that stops answering, which is strictly worse than a phone that forgets after a
/// crash that may never come.
/// </para>
/// </summary>
public interface IEngineStateStore
{
    /// <summary>The persisted snapshot, or an empty one when there is nothing usable.</summary>
    EngineStateSnapshot Load_OrEmpty();

    /// <summary>Replaces the persisted snapshot. Best effort — see the type's remarks.</summary>
    void Save(EngineStateSnapshot snapshot);
}
