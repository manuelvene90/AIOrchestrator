namespace AIOrchestratorCoreLib.GeneralSupervision.SetModelRequest;

public static class SetModelRequest_Factory
{
    public const string SUPERVISOR_ROLE = "supervisor";
    public const string IMPLEMENTER_ROLE = "implementer";

    public static ISetModelRequest Create(string orchId, string role, string model, string reason, string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(orchId) || string.IsNullOrWhiteSpace(model))
            throw new ArgumentException($"set-model needs orchId and model (got '{orchId}'/'{model}', file '{sourceFilePath}')");

        var normalizedRole = role.Trim().ToLowerInvariant();

        if (normalizedRole != SUPERVISOR_ROLE && normalizedRole != IMPLEMENTER_ROLE)
            throw new ArgumentException($"set-model role must be '{SUPERVISOR_ROLE}' or '{IMPLEMENTER_ROLE}', got '{role}' (file '{sourceFilePath}')");

        return new SetModelRequestModel(orchId, normalizedRole, model.Trim(), reason, sourceFilePath);
    }
}
