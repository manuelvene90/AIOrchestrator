namespace AIOrchestratorCoreLib.Bridge.ReceiptRegistry;

public static class ReceiptRegistry_Factory
{
    /// <summary>
    /// An empty registry: nothing survives a restart, and nothing needs to — a receipt older than the process has no delivery left to evolve it.
    /// The one thing a restart CAN cost is an edit staged behind a shut door (plan 03 Task 6c): it waits at most one per-message gap, so only
    /// a restart inside those seconds leaves that receipt on its previous text.
    /// </summary>
    public static IReceiptRegistry Create()
    {
        return new ReceiptRegistryModel();
    }
}
