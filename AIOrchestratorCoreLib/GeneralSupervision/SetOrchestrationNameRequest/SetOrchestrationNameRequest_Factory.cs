namespace AIOrchestratorCoreLib.GeneralSupervision.SetOrchestrationNameRequest;

public static class SetOrchestrationNameRequest_Factory
{
    /// <summary>Telegram topic names cap at 128 chars; the protocol wants 2-4 words anyway.</summary>
    const int MAX_NAME_LENGTH = 60;

    public static ISetOrchestrationNameRequest Create(string orchId, string name, string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(orchId) || string.IsNullOrWhiteSpace(name))
            throw new ArgumentException($"set-orchestration-name needs orchId and name (got '{orchId}'/'{name}', file '{sourceFilePath}')");

        var trimmed = name.Trim();

        if (trimmed.Length > MAX_NAME_LENGTH)
            trimmed = trimmed[..MAX_NAME_LENGTH];

        return new SetOrchestrationNameRequestModel(orchId, trimmed, sourceFilePath);
    }
}
