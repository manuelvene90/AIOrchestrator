using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Planning.PlanProgress;

namespace AIOrchestratorCoreLib.Bridge.Siblings;

/// <summary>
/// ONE SIBLING'S BLOCK OF <c>ENDEAVOUR.md</c>, AS READ — everything <see cref="EndeavourDigest_Builder"/>
/// prints about it, gathered by <see cref="EndeavourDigest_Reader"/>. The split is the
/// <see cref="SiblingWorld"/> one: the reader touches the disk, the builder is a pure function of this, so
/// every cap, cut and filter of the digest is testable without a file (the builder tests build these by
/// hand).
///
/// <para>
/// THE OWNER CHANNEL IS HANDED OVER WHOLE, not as a tail. "The last 6 owner or solo entries" is a filter
/// THEN a count, and both belong to the builder next to its constants: a reader that took the last six
/// entries first would hand over app entries, then filter them out, and print fewer than six. The outbox
/// is different — every entry there is a sibling's, so the reader cuts it with the builder's own
/// <see cref="EndeavourDigest_Builder.MAX_OUTBOX_SUBJECTS"/>, and the cap still has one home.
/// </para>
/// </summary>
/// <param name="name">The display name, or the orch id when there is none. Unsanitised: the builder puts it
/// through <see cref="EndeavourMarkers_Sync.Sanitise_Name"/>, the one sanitiser.</param>
/// <param name="orchId">The sibling's orchestration id.</param>
/// <param name="workingPath">Its tree, from <see cref="AIOrchestratorCoreLib.Sessions.WorkingPath_Resolver.Resolve"/> — the one
/// definition the launcher and the validator also read.</param>
/// <param name="branch">From <c>.git/HEAD</c> (<see cref="AIOrchestratorCoreLib.Git.GitHead_Reader"/>); null when HEAD is not a
/// branch or a sha the reader recognises, printed as <c>branch ?</c>.</param>
/// <param name="paused">Whether the owner paused it — printed as <c>paused</c> or <c>live</c>.</param>
/// <param name="progress">Its PLAN.md parsed, or null when it has none yet ("no task ledger yet").</param>
/// <param name="ownerChannelHistory">EVERY entry of its owner channel, live file AND archive, in order
/// (<see cref="ChannelHistory_Counter.Read_Entries"/>, decision 13), app entries included. Not a tail: the
/// builder filters to owner/solo and takes the last six, so a compaction that moved them all into the archive
/// still shows them.</param>
/// <param name="outboxSubjects">The subjects of the last <see cref="EndeavourDigest_Builder.MAX_OUTBOX_SUBJECTS"/>
/// entries of its outbox, oldest first — subjects only, because the outbox itself is read incrementally by
/// the siblings it is addressed to.</param>
public sealed class SiblingDigestInput(string name, string orchId, string workingPath, string? branch, bool paused, IPlanProgress? progress, IReadOnlyList<IChannelEntry> ownerChannelHistory, IReadOnlyList<string> outboxSubjects)
{
    public string Name { get; } = name;
    public string OrchId { get; } = orchId;
    public string WorkingPath { get; } = workingPath;
    public string? Branch { get; } = branch;
    public bool Paused { get; } = paused;
    public IPlanProgress? Progress { get; } = progress;
    public IReadOnlyList<IChannelEntry> OwnerChannelHistory { get; } = ownerChannelHistory;
    public IReadOnlyList<string> OutboxSubjects { get; } = outboxSubjects;
}
