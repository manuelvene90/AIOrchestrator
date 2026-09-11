namespace AIOrchestratorCoreLib.GeneralSupervision.StartOrchestrationRequest;

public static class StartOrchestrationRequest_Factory
{
    /// <summary>
    /// A BLANK TASK IS NO TASK. The reader hands over whatever the JSON held, and <c>"task": "  "</c>
    /// is a key somebody meant to fill: normalising it to null here means exactly one place decides
    /// what "the request carried a task" means, rather than every reader of the field guessing.
    /// </summary>
    public static IStartOrchestrationRequest Create(string repoQuery, bool? isBasic, string? task, string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(repoQuery))
            throw new ArgumentException($"Request repoQuery must be non-empty (file '{sourceFilePath}')");

        return new StartOrchestrationRequestModel(repoQuery, isBasic, string.IsNullOrWhiteSpace(task) ? null : task.Trim(), sourceFilePath);
    }
}
