namespace AIOrchestratorCoreLib.GeneralSupervision.MalformedRequest;

/// <summary>
/// A request file the reader could not accept, WITH the reason — agents write these files by
/// hand, so the log must say what was wrong (unknown action, missing field, unparseable JSON),
/// not just that a file was deleted.
/// </summary>
public interface IMalformedRequest
{
    string FilePath { get; }
    string Reason { get; }
}
