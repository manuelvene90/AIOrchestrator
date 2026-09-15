namespace AIOrchestratorCoreLib.Running.WakeTicket;

public static class WakeTicket_Factory
{
    /// <summary>
    /// A ticket is only ever written for a turn that IS starting (<see cref="WakeDecision.IWakeDecision"/>
    /// is never null when the sweep writes one), so a blank reason is a caller that lost it on the way —
    /// same rule as <see cref="WakeDecision.WakeDecision_Factory.Create"/>.
    /// </summary>
    public static IWakeTicket Create(int number, string reason, string? statePackFile, DateTime stampedUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A wake ticket needs the reason the turn is starting");

        return new WakeTicketModel(number, reason, statePackFile, stampedUtc);
    }
}
