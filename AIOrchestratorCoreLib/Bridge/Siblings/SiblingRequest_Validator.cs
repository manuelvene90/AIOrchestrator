using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.GeneralSupervision.SpawnSiblingRequest;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Status;

namespace AIOrchestratorCoreLib.Bridge.Siblings;

/// <summary>
/// THE REFUSAL TABLE for a <c>spawn-sibling</c> request (spec 2026-09-23 §4.2) — the facts about the
/// WORLD, which the reader deliberately left to the executor ("refused WITH ITS OWN REASON", the promote
/// precedent). Pure over a <see cref="SiblingWorld"/>, because it is asked three times per request — at
/// arrival, when the prompt is drawn, and at the tap — and a parked request can wait up to
/// <see cref="CloseConfirmation_Parking.EXPIRY_HOURS"/> hours between them. A sibling born from a stale
/// request is impossible only if the SAME rules run at the moment of effect.
///
/// <para>
/// THE ORDER, and one deliberate departure from the spec's table:
/// <c>unspawnable</c> → <c>not-a-solo</c> → <c>handover-already-used</c> → <c>no-handover-entry</c> →
/// <c>at-cap</c> → <c>worktree-not-absolute</c> → <c>worktree-missing</c> → <c>worktree-not-of-repo</c> →
/// <c>worktree-shared</c> → <c>name-taken</c>.
/// </para>
/// <para>
/// <c>handover-already-used</c> COMES BEFORE <c>at-cap</c> (and before <c>no-handover-entry</c>),
/// although §4.2 lists it after both. A successful birth adds one open member to the endeavour, so a
/// retry of the same file checked in the table's order would be answered <c>at-cap</c> — "close a
/// sibling" — sending the solo to ask the owner to close the very session its request just started. The
/// true answer is the idempotent one, "already started as &lt;id&gt;" (§4.4), and only this order gives
/// it. Recorded in the plan's pre-flight (2026-09-23) and pinned by
/// <c>ARetryAfterTheBirthThatFilledTheCap_IsAlreadyUsed_NotAtCap</c>.
/// </para>
/// <para>
/// <c>worktree-not-absolute</c> is not a row of the spec's table: it is the Task 6 carry (2026-09-23).
/// It sits first among the worktree checks because every later one would answer a relative path for the
/// app's own current directory.
/// </para>
/// </summary>
public static class SiblingRequest_Validator
{
    /// <summary>The one spelling of <c>bornFromHandover</c> — <c>"&lt;orch&gt;#&lt;n&gt;"</c>, the idempotency key (§3.2, §4.4).</summary>
    public static string Format_HandoverKey(string orchId, int index)
    {
        return $"{orchId}#{index}";
    }

    /// <summary>Null when every check passes; otherwise the archive label and the notice for the requester.</summary>
    public static (string Label, string Subject, string Body)? Decide_Refusal_OrNull(ISpawnSiblingRequest request, SiblingWorld world)
    {
        var requester = world.Requester;

        if (requester == null || requester.ClosedUtc != null)
            return Refuse(SiblingRefusals.UNSPAWNABLE, SiblingNotice_Wording.Describe_Unspawnable(request.OrchId));

        if (!OrchestrationShape.Is_BasicOrchestration(requester.SupervisorSpawnedUtc))
            return Refuse(SiblingRefusals.NOT_A_SOLO, SiblingNotice_Wording.Describe_NotASolo());

        var alreadyUsed = Decide_AlreadyUsed_OrNull(request, world);

        if (alreadyUsed != null)
            return alreadyUsed;

        if (!Has_HandoverEntry(request, world))
            return Refuse(SiblingRefusals.NO_HANDOVER_ENTRY, SiblingNotice_Wording.Describe_NoHandoverEntry(request.HandoverIndex));

        // THE ENDEAVOUR'S KEY is the requester's endeavour, or — before its first sibling, when nothing
        // has stamped it yet — its own id, which is exactly what the birth will stamp.
        var endeavourId = requester.EndeavourId ?? requester.OrchId;
        var openMembers = EndeavourMembers_Resolver.Resolve_All(world.Sessions, endeavourId)
            .Where(session => session.ClosedUtc == null)
            .ToList();

        // AN UNLINKED REQUESTER IS STILL ONE OPEN MEMBER: its EndeavourId is null, so the resolver — which
        // never lets null match null — does not count it.
        var openCount = EndeavourMembers_Resolver.Count_Open(world.Sessions, endeavourId) + (requester.EndeavourId == null ? 1 : 0);

        if (openCount >= world.MaxOpenMembers)
        {
            var others = openMembers.Where(session => !Is_Session(session, requester.OrchId)).ToList();
            return Refuse(SiblingRefusals.AT_CAP, SiblingNotice_Wording.Describe_AtCap(world.MaxOpenMembers, others));
        }

        if (!Path.IsPathFullyQualified(request.WorktreePath))
            return Refuse(SiblingRefusals.WORKTREE_NOT_ABSOLUTE, SiblingNotice_Wording.Describe_WorktreeNotAbsolute(request.WorktreePath));

        if (!world.WorktreeExists)
            return Refuse(SiblingRefusals.WORKTREE_MISSING, SiblingNotice_Wording.Describe_WorktreeMissing(request.WorktreePath));

        if (!world.RepoWorktreePaths.Any(path => WorkingPath_Comparer.Are_Same(path, request.WorktreePath)))
            return Refuse(SiblingRefusals.WORKTREE_NOT_OF_REPO, SiblingNotice_Wording.Describe_WorktreeNotOfRepo(request.WorktreePath, requester.RepoPath, world.RepoWorktreePaths));

        var sharedWith = Find_TreeHolder_OrNull(request.WorktreePath, requester, world);

        if (sharedWith != null)
            return Refuse(SiblingRefusals.WORKTREE_SHARED, SiblingNotice_Wording.Describe_WorktreeShared(request.WorktreePath, sharedWith));

        // THE REQUESTER'S OWN NAME COUNTS TOO: from the child's side the requester IS an open sibling, and
        // two topics with one name are two topics the owner cannot tell apart. Asked EXPLICITLY, because an
        // unlinked requester — every first birth — has no endeavour id yet and so is not in openMembers
        // (fix round 1, 2026-09-23: the comment claimed this while the code let a first child take its
        // parent's name).
        if (Is_SameName(requester.DisplayName, request.Name))
            return Refuse(SiblingRefusals.NAME_TAKEN, SiblingNotice_Wording.Describe_NameTaken(request.Name, requester.OrchId));

        var nameHolder = openMembers.FirstOrDefault(session => Is_SameName(session.DisplayName, request.Name));

        if (nameHolder != null)
            return Refuse(SiblingRefusals.NAME_TAKEN, SiblingNotice_Wording.Describe_NameTaken(request.Name, nameHolder.OrchId));

        return null;
    }

    /// <summary>
    /// ONE HANDOVER, AT MOST ONE SIBLING, EVER (§4.4). A CLOSED child still holds its handover: closing
    /// the job does not free its brief for a twin. A PARKED request citing the same entry holds it too —
    /// otherwise a re-drop while the owner is still deciding would put two prompts for one brief on the
    /// phone.
    /// </summary>
    static (string Label, string Subject, string Body)? Decide_AlreadyUsed_OrNull(ISpawnSiblingRequest request, SiblingWorld world)
    {
        var key = Format_HandoverKey(request.OrchId, request.HandoverIndex);

        var born = world.Sessions.FirstOrDefault(session => string.Equals(session.BornFromHandover, key, StringComparison.Ordinal));

        if (born != null)
            return Refuse(SiblingRefusals.HANDOVER_ALREADY_USED, SiblingNotice_Wording.Describe_HandoverAlreadyStarted(request.HandoverIndex, born));

        var held = world.OtherParkedSiblingRequests.Any(parked =>
            string.Equals(parked.OrchId, request.OrchId, StringComparison.Ordinal) && parked.HandoverIndex == request.HandoverIndex);

        if (held)
            return Refuse(SiblingRefusals.HANDOVER_ALREADY_USED, SiblingNotice_Wording.Describe_HandoverAlreadyHeld(request.HandoverIndex));

        return null;
    }

    /// <summary>
    /// The entry the request cites, written BY THE SOLO and carrying the marker — through the one matcher
    /// this repo has (<see cref="MemberState_Resolver.Contains_Marker"/>, whose position rules
    /// <see cref="HandoverEntry_Detector"/> relies on), never a second one. The author gate is the
    /// detector's: the owner quoting "HANDOVER" in a message is not the solo writing one down.
    /// </summary>
    static bool Has_HandoverEntry(ISpawnSiblingRequest request, SiblingWorld world)
    {
        return world.RequesterOutboxHistory.Any(entry =>
            entry.Index == request.HandoverIndex
            && entry.Author == ChannelAuthors.Solo
            && MemberState_Resolver.Contains_Marker(entry, HandoverEntry_Detector.HANDOVER_MARKER));
    }

    /// <summary>
    /// WHO ALREADY WORKS IN THAT TREE — anyone, not only this endeavour. Two sessions editing one checkout
    /// overwrite each other whether or not they are siblings, so every OPEN session in the world is asked
    /// (fix round 1, 2026-09-23: this used to scan only the requester's endeavour, so an open session of
    /// ANOTHER endeavour, or an unrelated solo of the same repo, could be handed a second writer).
    ///
    /// <para>
    /// GIT'S MAIN CHECKOUT IS ALWAYS TAKEN. <c>git worktree list</c> prints it first, and it is the tree
    /// the owner and every unlinked session of the repo work in — even when the requester's own RepoPath
    /// is itself a linked worktree, which is exactly when checking <c>requester.RepoPath</c> alone missed it.
    /// </para>
    /// <para>
    /// A CLOSED session's tree is free: its job is finished, and reusing a finished tree is ordinary.
    /// Every working path is read through <see cref="WorkingPath_Resolver"/> (decision 12).
    /// </para>
    /// </summary>
    static string? Find_TreeHolder_OrNull(string worktreePath, IOrchestrationSession requester, SiblingWorld world)
    {
        if (WorkingPath_Comparer.Are_Same(requester.RepoPath, worktreePath))
            return $"the repo itself — {requester.OrchId}'s checkout";

        if (world.RepoWorktreePaths.Count > 0 && WorkingPath_Comparer.Are_Same(world.RepoWorktreePaths[0], worktreePath))
            return "the repo's main checkout (git lists it first)";

        foreach (var session in world.Sessions)
        {
            if (session.ClosedUtc != null || !WorkingPath_Comparer.Are_Same(WorkingPath_Resolver.Resolve(session), worktreePath))
                continue;

            return Is_Session(session, requester.OrchId)
                ? $"your own working tree ({requester.OrchId})"
                : $"the working tree of {session.OrchId}, an open session";
        }

        return null;
    }

    static bool Is_SameName(string? displayName, string requestedName)
    {
        return displayName != null && string.Equals(displayName.Trim(), requestedName.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    static bool Is_Session(IOrchestrationSession session, string orchId)
    {
        return string.Equals(session.OrchId, orchId, StringComparison.Ordinal);
    }

    static (string Label, string Subject, string Body) Refuse(string label, (string Subject, string Body) notice)
    {
        return (label, notice.Subject, notice.Body);
    }
}
