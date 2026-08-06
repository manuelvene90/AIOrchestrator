using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;

namespace AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;

/// <summary>
/// Live access to the orchestrator configuration. config.json is edited AT RUNTIME — by the
/// Settings window and by the general supervisor seeding the repo list — so consumers must never
/// hold a startup snapshot; they ask the provider every time. Get_Current() reloads only when the
/// files actually changed and returns the SAME instance otherwise (reference equality works as
/// cheap change detection).
/// </summary>
public interface IOrchestratorConfigProvider
{
    IOrchestratorConfig Get_Current();
}
