namespace AIOrchestratorCoreLib.Running.SessionLaunch;

public static class SessionLaunch_Factory
{
    public const string SUPERVISOR_MEMBER_ID = "sup";
    public const string COMMUNICATOR_MEMBER_ID = "com";
    public const string GENERAL_MEMBER_ID = "general";

    /// <summary>
    /// <paramref name="effort"/> and <paramref name="resumeSessionId"/> are TRAILING AND OPTIONAL
    /// because most of what this app starts wants neither: only the supervisor and the solo carry an
    /// effort, and only those two ever resume a conversation of their own. A caller that says nothing
    /// gets the launch it always got — which is what keeps the general supervisor stateless and an
    /// implementer fresh by construction rather than by a call site remembering to pass null.
    /// </summary>
    public static ISessionLaunch Create(
        SessionRoles role,
        string orchId,
        string memberId,
        string workingDirectory,
        string? model,
        string pidFilePath,
        string? displayName,
        string? effort = null,
        string? resumeSessionId = null)
    {
        if (string.IsNullOrWhiteSpace(orchId))
            throw new ArgumentException($"Orchestration id must be non-empty (role {role}, member '{memberId}')");
        if (string.IsNullOrWhiteSpace(memberId))
            throw new ArgumentException($"Member id must be non-empty (role {role}, orchestration '{orchId}')");
        if (string.IsNullOrWhiteSpace(workingDirectory))
            throw new ArgumentException($"Working directory must be non-empty (role {role}, '{orchId}/{memberId}')");
        if (string.IsNullOrWhiteSpace(pidFilePath))
            throw new ArgumentException($"Pid file path must be non-empty (role {role}, '{orchId}/{memberId}')");

        return new SessionLaunchModel(role, orchId, memberId, workingDirectory, model, pidFilePath, displayName, effort, resumeSessionId);
    }
}
