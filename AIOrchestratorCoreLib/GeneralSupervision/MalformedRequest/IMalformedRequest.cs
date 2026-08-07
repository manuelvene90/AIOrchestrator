namespace AIOrchestratorCoreLib.GeneralSupervision.MalformedRequest;

/// <summary>
/// A request file the reader could not accept, WITH the reason — agents write these files by
/// hand, so the log must say what was wrong (unknown action, missing field, unparseable JSON),
/// not just that a file was deleted. When the orchestration is identifiable the rejection is
/// also written back into its channel, so the agent LEARNS instead of failing silently.
/// </summary>
public interface IMalformedRequest
{
    string FilePath { get; }
    string Reason { get; }

    /// <summary>Orchestration the file referred to, when parseable — null otherwise.</summary>
    string? OrchId { get; }
}
