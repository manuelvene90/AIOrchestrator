using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// The real launcher, plus two things no assertion can otherwise reach.
///
/// <para>
/// WHICH SHAPE was launched — <c>Start_Orchestration</c> and <c>Start_BasicOrchestration</c> are the
/// only difference between a crew and a solo, and reading it back off the session afterwards would be
/// asserting the store's opinion rather than the decision.
/// </para>
/// <para>
/// AND WHEN: a photograph of the new orchestration's owner channel taken at the instant the launch
/// returns. That instant is the boundary the task fix turns on — everything written before it is
/// history to the session just registered, everything after it is traffic.
/// </para>
/// </summary>
internal sealed class LaunchWitness_Fake(IOrchestrationLauncher inner, ISupervisionPaths paths) : IOrchestrationLauncher
{
    public string? StartedOrchId { get; private set; }

    /// <summary>The word for what was launched: <c>full</c> or <c>basic</c>; null until one was.</summary>
    public string? Shape { get; private set; }
    public string OwnerChannelWhenTheLaunchReturned { get; private set; } = string.Empty;

    public IOrchestrationSession Start_Orchestration(string repoName, string repoPath)
    {
        return Photograph(inner.Start_Orchestration(repoName, repoPath), OrchestrationModes.FULL);
    }

    public IOrchestrationSession Start_BasicOrchestration(string repoName, string repoPath)
    {
        return Photograph(inner.Start_BasicOrchestration(repoName, repoPath), OrchestrationModes.BASIC);
    }

    IOrchestrationSession Photograph(IOrchestrationSession session, string shape)
    {
        var file = paths.Get_OwnerChannelFile(session.OrchId);

        StartedOrchId = session.OrchId;
        Shape = shape;
        OwnerChannelWhenTheLaunchReturned = File.Exists(file) ? File.ReadAllText(file) : string.Empty;

        return session;
    }

    public IOrchestrationSession Add_Implementer(string orchId) => inner.Add_Implementer(orchId);
    public IOrchestrationSession Promote_ToFullCrew(string orchId) => inner.Promote_ToFullCrew(orchId);
    public IOrchestrationSession Demote_ToBasic(string orchId) => inner.Demote_ToBasic(orchId);
    public IOrchestrationSession Add_Member(string orchId, MemberKinds kind) => inner.Add_Member(orchId, kind);
    public IOrchestrationSession Add_Member(string orchId, MemberKinds kind, string? model) => inner.Add_Member(orchId, kind, model);
    public void Respawn_Supervisor(string orchId) => inner.Respawn_Supervisor(orchId);
    public void Respawn_Communicator(string orchId) => inner.Respawn_Communicator(orchId);
    public void Respawn_Implementer(string orchId, string memberId) => inner.Respawn_Implementer(orchId, memberId);
    public void Spawn_GeneralSupervisor() => inner.Spawn_GeneralSupervisor();
}
