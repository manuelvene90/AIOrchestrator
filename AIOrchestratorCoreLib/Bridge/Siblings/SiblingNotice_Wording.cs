using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.GeneralSupervision.SpawnSiblingRequest;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;

namespace AIOrchestratorCoreLib.Bridge.Siblings;

/// <summary>
/// EVERY SENTENCE THE APP SAYS ABOUT A <c>spawn-sibling</c> REQUEST, in one place. The validator runs at
/// arrival, at prompt time and at the tap (spec 2026-09-23 §4.2), and the executor sends what it
/// returns: wording spelled at each of those call sites would drift the way decision 12's second
/// duration formatter did.
///
/// <para>
/// AUDIENCE: every notice here goes to the REQUESTER as an <c>[agent]</c> entry, never to the owner's
/// phone (decision 15 — the owner cannot act on a session's malformed request, and the promote
/// precedent's own comment records what an Owner audience cost). The one exception is
/// <see cref="Describe_Unspawnable"/>, which has no requester channel to go to and is a general-channel
/// failure line, as the promote case's is.
/// </para>
/// <para>
/// EVERY REFUSAL ENDS WITH <see cref="NOTHING_CHANGED"/> — the promote precedent. It is the sentence that
/// stops a re-drop loop: the session learns the refusal cost nothing and that nobody is waiting on it.
/// </para>
/// </summary>
public static class SiblingNotice_Wording
{
    public const string NOTHING_CHANGED = "The owner has NOT been asked and nothing was changed.";

    public static (string Subject, string Body) Describe_Held(ISpawnSiblingRequest request)
    {
        return (
            $"sibling HELD — the owner confirms '{request.Name}' with a tap",
            $"Nothing has started yet and you are still the only session on this job. The owner has been asked to start '{request.Name}' for:\n\n{request.Job}\n\n"
            + $"Reason relayed: {request.Reason}\n\n"
            + $"You will get an entry here either way. If they do not answer within {CloseConfirmation_Parking.EXPIRY_HOURS} hours it lapses and you are told — do NOT re-drop it in the meantime, and carry on working.");
    }

    /// <summary>General-channel failure line: there is no open orchestration to address it to.</summary>
    public static (string Subject, string Body) Describe_Unspawnable(string orchId)
    {
        return (
            $"spawn-sibling FAILED: '{orchId}'",
            $"No open orchestration '{orchId}' — nothing was started.");
    }

    public static (string Subject, string Body) Describe_NotASolo()
    {
        return (
            "sibling REFUSED — this orchestration is a crew, not a solo",
            "A sibling is a second SOLO beside you, with its own topic, and this orchestration has a supervisor. A crew adds members with add-implementer instead.\n\n"
            + NOTHING_CHANGED);
    }

    /// <summary>
    /// THE IDEMPOTENT ANSWER (§4.4): one HANDOVER entry births at most one sibling, ever, so a retry of
    /// the same request is told what already happened — never refused as if something had gone wrong.
    /// </summary>
    public static (string Subject, string Body) Describe_HandoverAlreadyStarted(int handoverIndex, IOrchestrationSession sibling)
    {
        var state = sibling.ClosedUtc == null ? "" : $" ({sibling.OrchId} has since closed.)";

        return (
            $"sibling REFUSED — HANDOVER [{handoverIndex}] already started {sibling.OrchId}",
            $"HANDOVER entry [{handoverIndex}] of your sibling outbox was already started as {sibling.OrchId}.{state} This is the answer to a retry, not a new failure: one HANDOVER entry starts at most one sibling. A second sibling needs a second HANDOVER entry, with its own brief.\n\n"
            + NOTHING_CHANGED);
    }

    public static (string Subject, string Body) Describe_HandoverAlreadyHeld(int handoverIndex)
    {
        return (
            $"sibling REFUSED — HANDOVER [{handoverIndex}] is already waiting for the owner",
            $"A request citing HANDOVER entry [{handoverIndex}] is already held — do not re-drop. You will get an entry when the owner answers it or when it lapses.\n\n"
            + NOTHING_CHANGED);
    }

    public static (string Subject, string Body) Describe_NoHandoverEntry(int handoverIndex)
    {
        return (
            $"sibling REFUSED — no HANDOVER entry [{handoverIndex}] in your sibling outbox",
            $"The request cites entry [{handoverIndex}], and your sibling outbox (sibling-outbox.md, live file and archive) has no entry [{handoverIndex}] written by you that carries `{HandoverEntry_Detector.HANDOVER_MARKER}` in its subject or at the start of a body line.\n\n"
            + "The new sibling starts with nothing but that entry, so write it first — the job, the files it owns, the files you keep, the base branch — append it to YOUR OUTBOX with channel-append.sh, and cite the [n] the helper printed.\n\n"
            + NOTHING_CHANGED);
    }

    public static (string Subject, string Body) Describe_AtCap(int maxOpenMembers, IReadOnlyList<IOrchestrationSession> openSiblings)
    {
        var named = openSiblings.Count == 0
            ? "(no other open session — the cap allows only you)"
            : string.Join("\n", openSiblings.Select(sibling => $"- {sibling.OrchId} — {sibling.DisplayName ?? "(no name)"}"));

        return (
            $"sibling REFUSED — the endeavour already has {maxOpenMembers} open session(s), the cap",
            $"An endeavour may have at most {maxOpenMembers} open session(s), you included. Open now besides you:\n{named}\n\n"
            + "The owner can close one; ask again after that.\n\n"
            + NOTHING_CHANGED);
    }

    public static (string Subject, string Body) Describe_WorktreeNotAbsolute(string worktreePath)
    {
        return (
            "sibling REFUSED — the worktree path is not absolute",
            $"'{worktreePath}' is a relative path. The app would resolve it against its own folder, which is nobody's worktree, and the sibling would be spawned there on every respawn. Write the worktree's absolute path.\n\n"
            + NOTHING_CHANGED);
    }

    public static (string Subject, string Body) Describe_WorktreeMissing(string worktreePath)
    {
        return (
            "sibling REFUSED — the worktree does not exist",
            $"'{worktreePath}' is not a directory. Create the child's worktree and branch yourself first (git worktree add), then ask again — the app never runs git writes.\n\n"
            + NOTHING_CHANGED);
    }

    /// <summary>Names git's own list, which is also how a session that wrote another spelling of a real tree (an 8.3 short name) finds the one git uses.</summary>
    public static (string Subject, string Body) Describe_WorktreeNotOfRepo(string worktreePath, string repoPath, IReadOnlyList<string> listedWorktrees)
    {
        var listed = listedWorktrees.Count == 0
            ? "(git listed none — is the repo still a git repository?)"
            : string.Join("\n", listedWorktrees.Select(path => $"- {path}"));

        return (
            "sibling REFUSED — the worktree is not a worktree of your repo",
            $"'{worktreePath}' is not in `git worktree list` for '{repoPath}'. git lists:\n{listed}\n\n"
            + NOTHING_CHANGED);
    }

    public static (string Subject, string Body) Describe_WorktreeShared(string worktreePath, string holder)
    {
        return (
            "sibling REFUSED — that tree is already in use",
            $"'{worktreePath}' is {holder}. Siblings never share a tree: two sessions editing one checkout overwrite each other. Give the child its own worktree.\n\n"
            + NOTHING_CHANGED);
    }

    public static (string Subject, string Body) Describe_NameTaken(string name, string holderOrchId)
    {
        return (
            $"sibling REFUSED — the name '{name}' is taken",
            $"'{name}' is already the name of {holderOrchId}, an open session of this endeavour. Two topics with one name are two topics the owner cannot tell apart. Pick another name.\n\n"
            + NOTHING_CHANGED);
    }

    // ------------------------------------------------------------------------------- the birth (§4.3)
    //
    // THE THREE LINES BELOW ARE NOT REFUSALS, and two of them break the Agent-audience rule above on
    // purpose: the birth note and the parent's "started" line are what the owner tapped for, so they are
    // Owner-audience (global constraint, decision 15's test — the owner acts on them by talking to the
    // new topic). The general line is Agent: the owner already has both of the others on the phone.

    /// <summary>
    /// THE FIRST MESSAGE IN THE NEW TOPIC (§2.1, §4.3 step 4), split by WHO READS WHICH HALF.
    ///
    /// <para>
    /// THE SUBJECT IS THE OWNER'S. An app entry reaches the phone as <c>"⚙ App: " + subject</c> and its
    /// body never leaves the channel (<c>MirrorText_Formatter</c>: "the body is agent-facing detail the
    /// owner explicitly does not want texted"). The first draft put the job and "Write here about this
    /// job only" in the body, so the owner's first message in the new topic read only "🔗 Sibling of …"
    /// (review of 8a3e2e0, 2026-09-23). So whose sibling, the job and where to write are the subject.
    /// </para>
    /// <para>
    /// THE BODY IS THE CHILD'S: where its brief is — the parent's outbox path and the HANDOVER entry's
    /// index — because that entry is the only thing it starts from, and a path with no index would send
    /// it reading a file a whole endeavour writes into. A local path is nothing the owner should see.
    /// </para>
    /// </summary>
    public static (string Subject, string Body) Describe_BirthNote(string parentName, string job, string parentOutboxPath, int handoverIndex)
    {
        return (
            $"🔗 Sibling of {parentName} — job: {job.TrimEnd().TrimEnd('.')}. Write here about this job only.",
            $"Your brief: {parentOutboxPath} entry [{handoverIndex}]");
    }

    /// <summary>The requesting topic's one line (§2.1): the owner sees that the tap worked, and where to look next.</summary>
    public static (string Subject, string Body) Describe_ParentStarted(string childId, string childName)
    {
        return (
            $"sibling '{childId}' started — {childName} (its own topic)",
            $"The owner talks to '{childName}' in its own topic. You and it write to each other only through your sibling outboxes; carry on with your own job.");
    }

    /// <summary>
    /// The general supervisor's record (§4.3 step 5). It tracks orchestrations, so the subject opens the
    /// way <c>start-orchestration</c>'s does — "orchestration '&lt;id&gt;' started" — and says in the same
    /// breath whose sibling it is, which is the fact that line has never had to carry.
    /// </summary>
    public static (string Subject, string Body) Describe_GeneralStarted(string childId, string childName, string parentId, string parentName, string workingPath)
    {
        return (
            $"orchestration '{childId}' started — a sibling of '{parentId}'",
            $"'{childId}' ({childName}) was started on the owner's tap as a sibling of '{parentId}' ({parentName}), in its own worktree '{workingPath}' and its own topic. "
            + "They share an endeavour: closing one never closes the other.");
    }
}
