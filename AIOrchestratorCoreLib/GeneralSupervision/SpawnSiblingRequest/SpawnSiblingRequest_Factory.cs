namespace AIOrchestratorCoreLib.GeneralSupervision.SpawnSiblingRequest;

public static class SpawnSiblingRequest_Factory
{
    /// <summary>
    /// Throws on an empty orch id, the <c>PromoteOrchestrationRequest_Factory</c> precedent: the reader
    /// refuses that case with a reason first, so reaching this with one is a caller bug, not bad input.
    /// </summary>
    public static ISpawnSiblingRequest Create(
        string orchId,
        string name,
        string job,
        int handoverIndex,
        string worktreePath,
        string reason,
        string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(orchId))
            throw new ArgumentException($"Request orchId must be non-empty (file '{sourceFilePath}')");

        return new SpawnSiblingRequestModel(orchId, name, job, handoverIndex, worktreePath, reason, sourceFilePath);
    }
}
