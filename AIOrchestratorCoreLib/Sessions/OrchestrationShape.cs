namespace AIOrchestratorCoreLib.Sessions;

/// <summary>
/// Whether an orchestration has a SUPERVISOR SLOT — the only question anything actually asks about
/// its shape, and the one the watchdog needs an answer to before it respawns anything.
///
/// IT USED TO BE READ OFF THE MEMBER IDS: basic meant "some member id starts with solo-", derived
/// rather than stored so that session.json needed no migration and an orchestration could not end up
/// disagreeing with itself. That reasoning was right and the derivation still could not survive
/// PROMOTION, which is what replaced it:
///
///   - Member folders are audit trail and never leave the roster, closed ones included. Promote an
///     orchestration by closing its solo and spawning a supervisor, and it reads as basic FOR EVER —
///     so the promoted supervisor is never respawned when it dies, silently, with everything else
///     working.
///   - Filtering closed members does not fix it. An empty roster reads as NOT basic, so a basic
///     orchestration whose solo had been closed would flip to full and the watchdog would spawn a
///     supervisor into it — the exact failure the old check existed to prevent, reached from the
///     other side.
///
/// So the fact is read where it is actually recorded. `SupervisorSpawnedUtc` is stamped BEFORE the
/// spawn is attempted (`OrchestrationLauncherModel` — deliberately, so no tick can see "no pid file
/// and no grace" and double-spawn), it is already persisted, and it answers the question directly
/// instead of answering a question about members and hoping the two correlate. Still derived, still
/// no migration, still one source of truth.
///
/// KNOWN LIMIT, pinned by a test rather than left to be discovered: a session.json written before
/// that field existed carries no stamp, so an OPEN pre-field orchestration reads as basic and loses
/// its supervisor protection. Three such files exist on the owner's machine — all CLOSED, and the
/// watchdog skips closed orchestrations before it reaches this — so the gap is inert there. It is
/// unfixable from the data: an orchestration that never wrote down whether it had a supervisor is
/// genuinely indistinguishable from one that never had one.
/// </summary>
public static class OrchestrationShape
{
    /// <summary>
    /// A BASIC orchestration: one session talking straight to the owner, with no supervisor and none
    /// of the gates. Nothing may go looking for a supervisor in one — the watchdog would respawn one
    /// forever, on top of the solo session that IS the orchestration.
    /// </summary>
    public static bool Is_BasicOrchestration(DateTime? supervisorSpawnedUtc)
    {
        return supervisorSpawnedUtc == null;
    }
}
