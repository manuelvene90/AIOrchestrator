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
/// no archive to write, by the rule above.
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

        var child = launcher.Start_SiblingOrchestration(
            parent.OrchId,
            request.Name,
            request.WorktreePath,
            SiblingRequest_Validator.Format_HandoverKey(parent.OrchId, request.HandoverIndex));

        var endeavourId = child.EndeavourId
            ?? throw new Exception($"Sibling '{child.OrchId}' of '{parent.OrchId}' was started with no endeavour id — the launcher links every child it starts");

        if (store.Get_Session(parent.OrchId).EndeavourId == null)
            store.Set_EndeavourId(parent.OrchId, endeavourId);

        return (
            child,
            SiblingNotice_Wording.Describe_BirthNote(parentName, request.Job, paths.Get_SiblingOutboxFile(parent.OrchId), request.HandoverIndex),
            SiblingNotice_Wording.Describe_ParentStarted(child.OrchId, request.Name),
            SiblingNotice_Wording.Describe_GeneralStarted(child.OrchId, request.Name, parent.OrchId, parentName, request.WorktreePath));
    }
}
