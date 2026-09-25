using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.GeneralSupervision.SpawnSiblingRequest;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;

namespace AIOrchestratorCoreLib.Bridge.Siblings;

/// <summary>
/// EVERYTHING <see cref="SiblingRequest_Validator"/> JUDGES, gathered once by
/// <see cref="SiblingWorld_Reader"/>. Split this way so the table is PURE: it is asked at arrival, when
/// the prompt is drawn and again at the tap (spec 2026-09-23 §4.2, "checked at the tap, not only at
/// arrival"), and a validator that read the disk itself could not be tested row by row — nor could it
/// be shown to give the same answer to the same world.
/// </summary>
/// <param name="requester">The session named by the request's orchId, from the store; null when there is none.</param>
/// <param name="sessions">This read's <c>Load_All()</c> — open and closed, because a closed sibling still holds its handover (§4.4).</param>
/// <param name="requesterOutboxHistory">The requester's sibling outbox across live file AND archive (decision 13); empty when unknown.</param>
/// <param name="otherParkedSiblingRequests">Every parked spawn-sibling request except the one being judged.</param>
/// <param name="repoWorktreePaths">`git worktree list` for the requester's repo, as git spells them — the repo itself first.</param>
/// <param name="worktreeExists">The requested worktree is an absolute path to an existing directory.</param>
/// <param name="maxOpenMembers">`endeavour.maxOpenSiblings`: open members, the requester included (O2).</param>
public sealed class SiblingWorld(
    IOrchestrationSession? requester,
    IReadOnlyList<IOrchestrationSession> sessions,
    IReadOnlyList<IChannelEntry> requesterOutboxHistory,
    IReadOnlyList<ISpawnSiblingRequest> otherParkedSiblingRequests,
    IReadOnlyList<string> repoWorktreePaths,
    bool worktreeExists,
    int maxOpenMembers)
{
    public IOrchestrationSession? Requester { get; } = requester;
    public IReadOnlyList<IOrchestrationSession> Sessions { get; } = sessions;
    public IReadOnlyList<IChannelEntry> RequesterOutboxHistory { get; } = requesterOutboxHistory;
    public IReadOnlyList<ISpawnSiblingRequest> OtherParkedSiblingRequests { get; } = otherParkedSiblingRequests;
    public IReadOnlyList<string> RepoWorktreePaths { get; } = repoWorktreePaths;
    public bool WorktreeExists { get; } = worktreeExists;
    public int MaxOpenMembers { get; } = maxOpenMembers;
}
