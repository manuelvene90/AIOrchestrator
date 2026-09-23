namespace AIOrchestratorCoreLib.Configuration.EndeavourSettings;

internal sealed class EndeavourSettingsModel(int maxOpenSiblings) : IEndeavourSettings
{
    public int MaxOpenSiblings { get; } = maxOpenSiblings;
}
