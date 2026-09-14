using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.TurnSource;

namespace AIOrchestratorCoreLib.Running.StatePack;

/// <summary>
/// Everything the pack is built from, gathered by <see cref="StatePackInputs_Reader"/> and rendered
/// by <see cref="StatePack_Builder"/>. Immutable on purpose: the reader reads, the builder writes
/// text, and nothing in between edits what was read. Any field the reader could not fill is null
/// or empty AND named in <see cref="Unavailable"/>, so the session sees "the ledger could not be
/// read: <why>" rather than an absence it would mistake for "there is no ledger".
/// </summary>
public sealed class StatePackInputs(
    string orchId,
    string memberId,
    SessionRoles role,
    string requestId,
    IReadOnlyList<PendingEntry> pending,
    IReadOnlyList<ITurnSource> sources,
    IChannelEntry? brief,
    IChannelEntry? lastOwnEntry,
    IReadOnlyList<string> ledgerLines,
    string? planText,
    IReadOnlyList<string> gitLines,
    IReadOnlyList<IChannelEntry> ownerTail,
    IReadOnlyList<string> unavailable,
    string? progressNote = null)
{
    public string OrchId { get; } = orchId;
    public string MemberId { get; } = memberId;
    public SessionRoles Role { get; } = role;
    public string RequestId { get; } = requestId;
    public IReadOnlyList<PendingEntry> Pending { get; } = pending;
    public IReadOnlyList<ITurnSource> Sources { get; } = sources;

    /// <summary>The entry the member works from — <see cref="Brief_Finder"/>; null when the channel has none.</summary>
    public IChannelEntry? Brief { get; } = brief;

    /// <summary>The member's own latest entry: its report is its state.</summary>
    public IChannelEntry? LastOwnEntry { get; } = lastOwnEntry;

    /// <summary>The PLAN.md ledger lines that name the member (members); empty for the supervisor, which gets <see cref="PlanText"/> whole.</summary>
    public IReadOnlyList<string> LedgerLines { get; } = ledgerLines;

    /// <summary>The whole PLAN.md — supervisor and solo only.</summary>
    public string? PlanText { get; } = planText;

    /// <summary>One line per working tree: branch, dirty count, last commits.</summary>
    public IReadOnlyList<string> GitLines { get; } = gitLines;

    /// <summary>The last owner-channel entries — supervisor and solo only, because the owner's messages refer back to earlier ones.</summary>
    public IReadOnlyList<IChannelEntry> OwnerTail { get; } = ownerTail;

    /// <summary>"<section>: <why>" for every input the reader could not produce.</summary>
    public IReadOnlyList<string> Unavailable { get; } = unavailable;

    /// <summary>The member's own progress note (<see cref="StatePack_Locator.PROGRESS_FILE_NAME"/>), null when it kept none.</summary>
    public string? ProgressNote { get; } = progressNote;
}
