using AIOrchestratorCoreLib.Running;

namespace AIOrchestratorCoreLib.Bridge.LimitRescue;

/// <summary>
/// STOPS TERMINAL SESSIONS STUCK ON A USAGE LIMIT so the watchdog's ordinary dead-session path
/// brings them back (owner, 2026-09-23: <i>"it would be ideal to have all sessions restarting
/// automatically when limits reset. And even worse, I sent a /resume command to the gen sup, and not
/// all got awakened."</i>). Moved out of <c>BridgeEngineModel</c>, where b2b7d8a first put it.
///
/// <para>
/// TWO WAYS IN, ONE SLOT LIST. <see cref="Rescue_Due"/> is the mirror tick's: it restarts a session
/// only on the grounds <see cref="Limits.LimitRescue_Decider"/> names — the reset its refusal named
/// has passed, a fresh probe shows the allowance back, or the refusal predates an account switch.
/// <see cref="Rescue_AllBlocked_Now"/> is /resume's: the owner has said the limit reset, so every
/// blocked, running session is stopped with no grace and no interval. Both skip a closed or paused
/// orchestration (the watchdog would not bring it back), a bridge-driven session (its dispatcher
/// re-runs the turn at the reset), a session that is not blocked (a restart must never interrupt live
/// work), one that is not running (the watchdog is already bringing it back) and one whose process
/// started after the refusal it shows (a respawn still starting up). Both record the restart against
/// the same half-hour cap.
/// </para>
/// <para>
/// The delegates are the engine's, passed per call because they read its live state: whether a slot
/// is bridge-driven is read from config per tick, and the probe reading is the engine's one reader of
/// the usage files (decision 12).
/// </para>
/// </summary>
public interface ILimitRescuer
{
    /// <param name="accountSwitchedAtUtc">When the owner last logged in to a different Claude account, or null.</param>
    /// <param name="isBridgeDriven">(role, orchId, memberId) → the slot is run by the dispatcher, not a terminal.</param>
    /// <param name="freshProbeShowsAllowanceSince">A probe written at or after this instant shows every live window below its limit.</param>
    void Rescue_Due(
        DateTime nowUtc,
        DateTime dispatchOpenSinceUtc,
        DateTime? accountSwitchedAtUtc,
        Func<SessionRoles, string, string, bool> isBridgeDriven,
        Func<DateTime, bool> freshProbeShowsAllowanceSince);

    LimitRescueSweep Rescue_AllBlocked_Now(DateTime nowUtc, Func<SessionRoles, string, string, bool> isBridgeDriven);
}
