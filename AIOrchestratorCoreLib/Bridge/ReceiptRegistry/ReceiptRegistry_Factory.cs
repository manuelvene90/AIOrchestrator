namespace AIOrchestratorCoreLib.Bridge.ReceiptRegistry;

public static class ReceiptRegistry_Factory
{
    /// <summary>An empty registry: nothing survives a restart, and nothing needs to — a receipt older than the process has no delivery left to evolve it.</summary>
    public static IReceiptRegistry Create()
    {
        return new ReceiptRegistryModel();
    }
}
