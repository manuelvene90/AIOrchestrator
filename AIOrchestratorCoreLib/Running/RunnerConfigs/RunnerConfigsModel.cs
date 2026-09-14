using AIOrchestratorCoreLib.Running.RoleRunnerConfig;

namespace AIOrchestratorCoreLib.Running.RunnerConfigs;

internal sealed class RunnerConfigsModel(
    IReadOnlyDictionary<SessionRoles, IRoleRunnerConfig> roles,
    int maxConcurrentTurns,
    int maxConcurrentTurnsPerOrchestration,
    TimeSpan turnTimeout,
    TimeSpan coalesceWindow,
    TimeSpan memberDigestWindow,
    TimeSpan silenceLimit,
    string sessionMemoryMax,
    IReadOnlyList<string> rejections,
    TimeSpan memberSilenceLimit,
    TimeSpan memberTurnTimeout) : IRunnerConfigs
{
    readonly IReadOnlyDictionary<SessionRoles, IRoleRunnerConfig> _roles = roles;

    public int MaxConcurrentTurns { get; } = maxConcurrentTurns;
    public int MaxConcurrentTurnsPerOrchestration { get; } = maxConcurrentTurnsPerOrchestration;
    public TimeSpan TurnTimeout { get; } = turnTimeout;
    public TimeSpan CoalesceWindow { get; } = coalesceWindow;
    public TimeSpan MemberDigestWindow { get; } = memberDigestWindow;
    public TimeSpan SilenceLimit { get; } = silenceLimit;
    public TimeSpan MemberSilenceLimit { get; } = memberSilenceLimit;
    public TimeSpan MemberTurnTimeout { get; } = memberTurnTimeout;
    public string SessionMemoryMax { get; } = sessionMemoryMax;
    public IReadOnlyList<string> Rejections { get; } = rejections;

    public IRoleRunnerConfig Get_ForRole(SessionRoles role)
    {
        return _roles.TryGetValue(role, out var config) ? config : RoleRunnerConfig_Factory.Create_Default(role);
    }
}
