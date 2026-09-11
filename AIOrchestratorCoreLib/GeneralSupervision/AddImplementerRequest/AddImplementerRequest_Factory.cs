using AIOrchestratorCoreLib.Sessions;

namespace AIOrchestratorCoreLib.GeneralSupervision.AddImplementerRequest;

public static class AddImplementerRequest_Factory
{
    public static IAddImplementerRequest Create(string orchId, MemberKinds kind, string reason, string sourceFilePath)
    {
        return Create(orchId, kind, reason, sourceFilePath, null);
    }

    public static IAddImplementerRequest Create(string orchId, MemberKinds kind, string reason, string sourceFilePath, string? model)
    {
        if (string.IsNullOrWhiteSpace(orchId))
            throw new ArgumentException($"Request orchId must be non-empty (file '{sourceFilePath}')");

        return new AddImplementerRequestModel(orchId, kind, reason, sourceFilePath, string.IsNullOrWhiteSpace(model) ? null : model.Trim());
    }
}
