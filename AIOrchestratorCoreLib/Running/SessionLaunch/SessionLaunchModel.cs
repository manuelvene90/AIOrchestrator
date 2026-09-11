namespace AIOrchestratorCoreLib.Running.SessionLaunch;

internal sealed class SessionLaunchModel(
    SessionRoles role,
    string orchId,
    string memberId,
    string workingDirectory,
    string? model,
    string pidFilePath,
    string? displayName) : ISessionLaunch
{
    public SessionRoles Role { get; } = role;
    public string OrchId { get; } = orchId;
    public string MemberId { get; } = memberId;
    public string WorkingDirectory { get; } = workingDirectory;
    public string? Model { get; } = model;
    public string PidFilePath { get; } = pidFilePath;
    public string? DisplayName { get; } = displayName;
}
