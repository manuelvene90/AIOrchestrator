namespace AIOrchestratorCoreLib.Composition.HostOptions;

internal sealed class HostOptionsModel(string supervisionRoot, string claudeHome) : IHostOptions
{
    public string SupervisionRoot { get; } = supervisionRoot;
    public string ClaudeHome { get; } = claudeHome;
}
