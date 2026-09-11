namespace AIOrchestratorCoreLib.Running.SessionLaunch;

public static class SessionLaunch_Factory
{
    public const string SUPERVISOR_MEMBER_ID = "sup";
    public const string COMMUNICATOR_MEMBER_ID = "com";
    public const string GENERAL_MEMBER_ID = "general";

    public static ISessionLaunch Create(
        SessionRoles role,
        string orchId,
        string memberId,
        string workingDirectory,
        string? model,
        string pidFilePath,
        string? displayName)
    {
        if (string.IsNullOrWhiteSpace(orchId))
            throw new ArgumentException($"Orchestration id must be non-empty (role {role}, member '{memberId}')");
        if (string.IsNullOrWhiteSpace(memberId))
            throw new ArgumentException($"Member id must be non-empty (role {role}, orchestration '{orchId}')");
        if (string.IsNullOrWhiteSpace(workingDirectory))
            throw new ArgumentException($"Working directory must be non-empty (role {role}, '{orchId}/{memberId}')");
        if (string.IsNullOrWhiteSpace(pidFilePath))
            throw new ArgumentException($"Pid file path must be non-empty (role {role}, '{orchId}/{memberId}')");

        return new SessionLaunchModel(role, orchId, memberId, workingDirectory, model, pidFilePath, displayName);
    }
}
