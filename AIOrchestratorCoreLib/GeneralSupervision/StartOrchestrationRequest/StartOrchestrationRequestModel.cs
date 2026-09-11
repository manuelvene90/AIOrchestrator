namespace AIOrchestratorCoreLib.GeneralSupervision.StartOrchestrationRequest;

internal sealed class StartOrchestrationRequestModel(
    string repoQuery,
    bool? isBasic,
    string? task,
    string sourceFilePath) : IStartOrchestrationRequest
{
    public string RepoQuery { get; } = repoQuery;
    public bool? IsBasic { get; } = isBasic;
    public string? Task { get; } = task;
    public string SourceFilePath { get; } = sourceFilePath;
}
