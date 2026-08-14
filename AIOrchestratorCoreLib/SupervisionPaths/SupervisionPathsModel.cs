namespace AIOrchestratorCoreLib.SupervisionPaths;

internal sealed class SupervisionPathsModel(string root) : ISupervisionPaths
{
    public string Root { get; } = root;
    public string ConfigFile { get; } = Path.Combine(root, "config.json");
    public string SecretsFile { get; } = Path.Combine(root, "secrets.json");
    public string BridgeStateFile { get; } = Path.Combine(root, ".bridge-state.json");
    public string GlobalLogFile { get; } = Path.Combine(root, "orchestrator-global.log.jsonl");
    public string GeneralFolder { get; } = Path.Combine(root, "general");
    public string GeneralChannelFile { get; } = Path.Combine(root, "general", "channel.md");
    public string GeneralPidFile { get; } = Path.Combine(root, "general", ".pid");
    public string RequestsFolder { get; } = Path.Combine(root, ".requests");
    public string LimitAlertStateFile { get; } = Path.Combine(root, ".limit-alerts.json");
    public string GeneralDashboardStateFile { get; } = Path.Combine(root, ".general-dashboard.json");

    public string Get_OrchestrationFolder(string orchId)
    {
        return Path.Combine(Root, orchId);
    }

    public string Get_SessionFile(string orchId)
    {
        return Path.Combine(Get_OrchestrationFolder(orchId), "session.json");
    }

    public string Get_PlanFile(string orchId)
    {
        return Path.Combine(Get_OrchestrationFolder(orchId), "PLAN.md");
    }

    public string Get_ProgressFile(string orchId)
    {
        return Path.Combine(Get_OrchestrationFolder(orchId), ".progress.json");
    }

    public string Get_OwnerChannelFile(string orchId)
    {
        return Path.Combine(Get_OrchestrationFolder(orchId), "owner-channel.md");
    }

    public string Get_OrchestrationLogFile(string orchId)
    {
        return Path.Combine(Get_OrchestrationFolder(orchId), "orchestrator.log.jsonl");
    }

    public string Get_SupervisorPidFile(string orchId)
    {
        return Path.Combine(Get_OrchestrationFolder(orchId), ".supervisor.pid");
    }

    public string Get_CommunicatorPidFile(string orchId)
    {
        return Path.Combine(Get_OrchestrationFolder(orchId), ".communicator.pid");
    }

    public string Get_ImplementerPidFile(string orchId, string memberId)
    {
        return Path.Combine(Get_ImplementerFolder(orchId, memberId), ".pid");
    }

    public string Get_ImplementerFolder(string orchId, string memberId)
    {
        return Path.Combine(Get_OrchestrationFolder(orchId), memberId);
    }

    public string Get_ImplementerChannelFile(string orchId, string memberId)
    {
        return Path.Combine(Get_ImplementerFolder(orchId, memberId), "channel.md");
    }
}
