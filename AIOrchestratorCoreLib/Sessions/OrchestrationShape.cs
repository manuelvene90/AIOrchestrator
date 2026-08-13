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

    /// <summary>
    /// May this orchestration take a new member of this kind? A BASIC one takes only its solo, and a
    /// CREW takes anything but a solo.
    ///
    /// THIS IS THE ENFORCEMENT BEHIND THE PROMOTION GATE, and without it the gate had a door beside
    /// it. The desktop's "+ Implementer" button calls straight into the launcher, so a click on a
    /// basic card produced a solo plus an implementer and NO supervisor: no request, no handover
    /// entry, no owner tap — the three things the promotion path spends a request, a park, a prompt
    /// and a tap to enforce. Worse, nothing stamps `SupervisorSpawnedUtc` on that path, so the
    /// orchestration still reads as basic, the watchdog never spawns a supervisor, and the new
    /// implementer sits on its spoke waiting for a brief that cannot come from anywhere.
    ///
    /// Decision 21 is why it lives HERE rather than in the button's visibility: a session or a click
    /// can only ask, and the enforcement that must actually hold belongs at the point of effect. The
    /// UI may still hide the button — that is a courtesy, not the guard.
    ///
    /// PROMOTION PASSES THIS UNAIDED, which is not a coincidence and is worth stating: the supervisor
    /// is spawned FIRST, so the stamp exists by the time `Promote_ToFullCrew` adds imp-1, and the same
    /// rule that refuses the button admits the promotion. If that ordering is ever changed, this rule
    /// is what will stop it.
    /// </summary>
    public static bool Can_AddMember(DateTime? supervisorSpawnedUtc, MemberKinds kind)
    {
        return Is_BasicOrchestration(supervisorSpawnedUtc)
            ? kind == MemberKinds.Solo
            : kind != MemberKinds.Solo;
    }

    /// <summary>
    /// Why the member was refused, in words the owner reads in a dialog and an agent reads in a
    /// channel — naming the SUPPORTED path rather than only the refusal. "Not allowed" tells somebody
    /// they are stuck; this tells them what to do instead.
    /// </summary>
    public static string Describe_AddMemberRefusal(DateTime? supervisorSpawnedUtc, MemberKinds kind)
    {
        return Is_BasicOrchestration(supervisorSpawnedUtc)
            ? $"This is a BASIC orchestration — one session, no supervisor — so a {kind.ToString().ToLowerInvariant()} cannot be added to it. "
              + "An implementer here would wait for a brief from a supervisor that does not exist and cannot be created. "
              + "To get a crew, the session itself asks for a promotion (it files a handover entry first) and you confirm it with a tap."
            : $"This orchestration already has a supervisor, so a {kind.ToString().ToLowerInvariant()} cannot be added to it — a solo exists only in a basic orchestration.";
    }
}
