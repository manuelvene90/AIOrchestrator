namespace AIOrchestratorCoreLib.Running.WakeTicket;

internal sealed class WakeTicketModel(int number, string reason, string? statePackFile, DateTime stampedUtc) : IWakeTicket
{
    public int Number { get; } = number;
    public string Reason { get; } = reason;
    public string? StatePackFile { get; } = statePackFile;
    public DateTime StampedUtc { get; } = stampedUtc;
}
