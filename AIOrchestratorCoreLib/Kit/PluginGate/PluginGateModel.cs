namespace AIOrchestratorCoreLib.Kit.PluginGate;

internal sealed class PluginGateModel : IPluginGate
{
    public PluginVerdicts Verdict { get; private set; } = PluginVerdicts.Unchecked;
    public string? Refusal { get; private set; }

    /// <summary>
    /// Unchecked ALLOWS. Not an oversight and not a loophole: this host's own startup records a
    /// verdict before it takes any owner traffic, so Unchecked at spawn time means the check never
    /// ran — a bug in wiring, not a bad kit. Refusing there would strand a working machine over a
    /// question nobody asked, while allowing costs exactly what the old behaviour cost. It is never
    /// SILENT: the startup log says the gate never ran, per decision 21's corollary.
    /// </summary>
    public bool Spawning_Allowed => Verdict is PluginVerdicts.Unchecked or PluginVerdicts.Ok;

    /// <summary>
    /// Flipped by the first caller that reports an Unchecked pass, so the warning is stated once
    /// rather than on every spawn — a repeat that is a notification is a waterfall (decision 14).
    /// </summary>
    public bool Unchecked_WasReported { get; set; }

    public void Record(PluginVerdicts verdict, string? refusal)
    {
        Verdict = verdict;
        Refusal = refusal;
    }
}
