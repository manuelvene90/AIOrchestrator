namespace AIOrchestratorCoreLib.GeneralSupervision.StartOrchestrationRequest;

internal sealed class StartOrchestrationRequestModel(
    string repoQuery,
    bool isBasic,
    string sourceFilePath) : IStartOrchestrationRequest
{
    public string RepoQuery { get; } = repoQuery;
    public bool IsBasic { get; } = isBasic;
    public string SourceFilePath { get; } = sourceFilePath;
}
