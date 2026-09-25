using AIOrchestratorCoreLib.GeneralSupervision.SpawnSiblingRequest;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Bridge.Siblings;

/// <summary>
/// THE BIRTH OF A SIBLING, once the owner has tapped and the request has been re-validated (spec
/// 2026-09-23 §4.3 steps 2-5): link the parent, launch the child, and hand back the three things the
/// engine must say about it.
///
/// <para>
/// IT APPENDS TO NO CHANNEL. <c>Append_OrchestrationAppEntry</c> raises activity and
/// <c>Raise_OwnerWait</c> holds the owner's answer credit — both are engine state, and the ORDER of the
/// appends is the mechanism (§4.3 step 4: after the launch, birth note first, then the job as FROM
/// owner). Returning the words keeps this testable without an engine and keeps that ordering rule in
/// one place, the engine's execute arm.
/// </para>
/// <para>
/// A DELIBERATE REORDER OF §4.3 STEPS 2 AND 3. The spec stamps the parent's endeavour id and then
/// launches. Done in that order, a launch that throws (the worktree vanished between the tap-time
/// re-validation and the call; a store write failed) leaves the parent linked to an endeavour of ONE —
/// and nothing ever unlinks it, so every endeavour surface would draw a group with a single member for
/// the rest of the parent's life. So the child is launched FIRST: the launcher already derives the
/// child's endeavour as <c>parent.EndeavourId ?? parent.OrchId</c> (Task 5) and never writes the parent.
/// Only after it returns is the parent stamped, and with the id the CHILD WAS GIVEN, read back from
/// the child — not a second computation of the same rule, which is how two answers to one question
/// start (decision 12). The end state is identical to the spec's order; the failure state is not.
/// </para>
/// <para>
/// IT THROWS ON LAUNCH FAILURE, and the engine catches — the step has no channel to report into and
/// no archive to write, by the rule above. Two failures, two end states: one BEFORE the child was
/// created (a missing worktree) leaves the parent unlinked and nothing on disk; one AFTER it (the spawn
/// threw) leaves a linked child, so the parent is linked to it too and the exception names it — see the
/// catch below (review of 8a3e2e0, 2026-09-23).
/// </para>
/// </summary>
public static class SiblingBirth_Step
{
    public static (
        IOrchestrationSession Child,
        (string Subject, string Body) BirthNote,
        (string Subject, string Body) ParentNotice,
        (string Subject, string Body) GeneralLine) Execute(
            IOrchestrationSessionStore store,
            IOrchestrationLauncher launcher,
            ISupervisionPaths paths,
            ISpawnSiblingRequest request)
    {
        var parent = store.Get_Session(request.OrchId);
        var parentName = parent.DisplayName ?? parent.OrchId;
        var handoverKey = SiblingRequest_Validator.Format_HandoverKey(parent.OrchId, request.HandoverIndex);
        var existingIds = store.Load_All().Select(session => session.OrchId).ToHashSet(StringComparer.Ordinal);

        IOrchestrationSession child;

        try
        {
            child = launcher.Start_SiblingOrchestration(parent.OrchId, request.Name, request.WorktreePath, handoverKey);
        }
        catch (Exception ex)
        {
            // A FAILURE AFTER THE CHILD WAS CREATED is not a failure that created nothing. The launcher
            // writes the orchestration and its link BEFORE the spawn (the window title is read at spawn),
            // so a spawn that throws leaves a linked child carrying this handover key — and from then on
            // every retry is refused as handover-already-used naming it, so no later birth could ever
            // link the parent. The parent is linked to the child that exists, and the message names it,
            // because "the launch failed" alone reads as "nothing happened" and a session left running
            // with no job is exactly what the owner then needs to hear about.
            var created = store.Load_All().FirstOrDefault(session =>
                !existingIds.Contains(session.OrchId) && session.BornFromHandover == handoverKey);

            if (created == null)
                throw;

            Link_Parent_IfUnlinked(store, parent.OrchId, created);

            throw new Exception(
                $"Sibling '{created.OrchId}' of '{parent.OrchId}' was created and linked; its session did not start: {ex.Message}",
                ex);
        }

        Link_Parent_IfUnlinked(store, parent.OrchId, child);

        return (
            child,
            SiblingNotice_Wording.Describe_BirthNote(parentName, request.Job, paths.Get_SiblingOutboxFile(parent.OrchId), request.HandoverIndex),
            SiblingNotice_Wording.Describe_ParentStarted(child.OrchId, request.Name),
            SiblingNotice_Wording.Describe_GeneralStarted(child.OrchId, request.Name, parent.OrchId, parentName, request.WorktreePath));
    }

    /// <summary>
    /// THE WEDGE A CRASH MID-BIRTH LEAVES, repaired where it is found (final review M2, 2026-09-24). The
    /// launcher links and spawns the child BEFORE <see cref="Link_Parent_IfUnlinked"/> runs, so an app that
    /// dies between the two leaves a live child alone in endeavour <c>&lt;parent&gt;</c> and an unlinked parent.
    /// The parked request survives the restart and is re-checked — and refused
    /// <see cref="SiblingRefusals.HANDOVER_ALREADY_USED"/>, correctly, because the child exists. Nothing else
    /// would ever link the parent: neither would read the other's outbox for the rest of their lives.
    ///
    /// <para>
    /// So that refusal also links the parent to the child its handover already started — the same
    /// <see cref="Link_Parent_IfUnlinked"/> the birth uses, so the end state is exactly the birth's. Only when
    /// the child is OPEN (a closed child's parent has nothing left to be linked to) and the parent is open and
    /// still unlinked; otherwise null and nothing is written. Idempotent: the second call finds the parent
    /// linked. Returns the line for the caller's log when it repaired something.
    /// </para>
    /// </summary>
    public static string? Repair_ParentLink_OrNull(
        IOrchestrationSessionStore store,
        string parentOrchId,
        int handoverIndex,
        IReadOnlyList<IOrchestrationSession> sessions)
    {
        var key = SiblingRequest_Validator.Format_HandoverKey(parentOrchId, handoverIndex);
        var child = sessions.FirstOrDefault(session => string.Equals(session.BornFromHandover, key, StringComparison.Ordinal));

        if (child == null || child.ClosedUtc != null || child.EndeavourId == null)
            return null;

        var parent = store.Get_Session_OrNull(parentOrchId);

        if (parent == null || parent.ClosedUtc != null || parent.EndeavourId != null)
            return null;

        Link_Parent_IfUnlinked(store, parentOrchId, child);

        return $"'{parentOrchId}' was not linked to '{child.OrchId}', the sibling its HANDOVER [{handoverIndex}] already started (a birth interrupted between the spawn and the parent's link) — linked now to endeavour '{child.EndeavourId}'";
    }

    /// <summary>Stamps the parent with the endeavour id the CHILD carries — never a second derivation of it.</summary>
    static void Link_Parent_IfUnlinked(IOrchestrationSessionStore store, string parentOrchId, IOrchestrationSession child)
    {
        var endeavourId = child.EndeavourId
            ?? throw new Exception($"Sibling '{child.OrchId}' of '{parentOrchId}' has no endeavour id — the launcher links every child it creates");

        if (store.Get_Session(parentOrchId).EndeavourId == null)
            store.Set_EndeavourId(parentOrchId, endeavourId);
    }
}
