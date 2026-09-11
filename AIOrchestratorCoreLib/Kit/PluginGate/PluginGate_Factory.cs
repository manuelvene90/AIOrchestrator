namespace AIOrchestratorCoreLib.Kit.PluginGate;

public static class PluginGate_Factory
{
    /// <summary>The production shape: Unchecked until the host's startup records a verdict.</summary>
    public static IPluginGate Create()
    {
        return new PluginGateModel();
    }

    /// <summary>For tests and for hosts with no kit of their own: a gate that already said yes.</summary>
    public static IPluginGate Create_Allowing()
    {
        var gate = new PluginGateModel();
        gate.Record(PluginVerdicts.Ok, null);
        return gate;
    }
}
