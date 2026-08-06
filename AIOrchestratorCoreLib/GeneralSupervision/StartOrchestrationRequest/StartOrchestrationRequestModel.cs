namespace AIOrchestratorCoreLib.GeneralSupervision.StartOrchestrationRequest;

internal sealed class StartOrchestrationRequestModel(
    string repoQuery,
    string sourceFilePath) : IStartOrchestrationRequest
{
    public string RepoQuery { get; } = repoQuery;
    public string SourceFilePath { get; } = sourceFilePath;
}
