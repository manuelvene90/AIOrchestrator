using AIOrchestratorCoreLib.Configuration.PhoneSettings;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;

namespace AIOrchestratorCoreLib.Bridge.PeriodicStatus;

/// <summary>
/// THE APP'S UNPROMPTED STATUS POSTS — the periodic status (under <c>phone.status.periodic</c>) and
/// the away digest, each on its own slot, each posted only when it says something the last one did
/// not. Run once per bridge tick.
/// </summary>
public interface IPeriodicStatusSweep
{
    /// <param name="now">
    /// ONE reading of the LOCAL clock for the whole sweep. Taking it per session would let a sweep that
    /// straddles a boundary split the batch across two slots — the trickle, in miniature.
    /// </param>
    /// <param name="isAway">The app-wide away state, read once for the sweep.</param>
    /// <param name="phone">
    /// The phone block RESOLVED AT THE CALL SITE from <c>_configProvider.Get_Current()</c> on this tick
    /// — never cached here, because the provider re-reads config.json on its write stamp.
    /// </param>
    Task Push_Async(
        DateTime now,
        bool isAway,
        IPhoneSettings phone,
        IReadOnlyList<IOrchestrationSession> sessions,
        IPeriodicStatusHost host,
        CancellationToken cancellationToken);

    /// <summary>
    /// The away spell ended: the next one starts from nothing, so its FIRST digest always sends rather
    /// than being compared against a snapshot from hours ago and silently swallowed.
    /// </summary>
    void Forget_AwayDigests();
}
