namespace AIOrchestratorCoreLib.GeneralSupervision.SpawnSiblingRequest;

internal sealed class SpawnSiblingRequestModel(
    string orchId,
    string name,
    string job,
    int handoverIndex,
    string worktreePath,
    string reason,
    string sourceFilePath) : ISpawnSiblingRequest
{
    public string OrchId { get; } = orchId;
    public string Name { get; } = name;
    public string Job { get; } = job;
    public int HandoverIndex { get; } = handoverIndex;
    public string WorktreePath { get; } = worktreePath;
    public string Reason { get; } = reason;
    public string SourceFilePath { get; } = sourceFilePath;
}
