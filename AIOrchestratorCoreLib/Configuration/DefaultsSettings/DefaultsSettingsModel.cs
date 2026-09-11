namespace AIOrchestratorCoreLib.Configuration.DefaultsSettings;

internal sealed class DefaultsSettingsModel(bool orchestrationIsBasic) : IDefaultsSettings
{
    public bool OrchestrationIsBasic { get; } = orchestrationIsBasic;
}
