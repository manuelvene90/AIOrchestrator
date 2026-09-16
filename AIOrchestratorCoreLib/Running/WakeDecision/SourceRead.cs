using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.TurnSource;

namespace AIOrchestratorCoreLib.Running.WakeDecision;

/// <summary>
/// One source and what of it is waiting, as of this tick. It carries the PENDING ENTRIES AND NOTHING
/// ELSE on purpose: an earlier shape also held the file's whole contents and the cursor it was read
/// against, and neither had a reader — a snapshot nobody consumes is how the next person reasons
/// from a stale copy of a file that has since been appended to.
/// <c>PrintTurnDispatcherModel.Advance_Cursors</c> re-reads deliberately, and this leaves it nothing
/// to re-read from. The one caller that DOES need the cursor and the entries — the dispatcher's
/// cursor bookkeeping — is handed them while they are fresh, by
/// <see cref="WakeDecision_Resolver.SourceReadObserver"/>, and never through this record.
/// </summary>
/// <param name="NothingEverDelivered">
/// Whether this session has never been handed anything from the source — so everything pending on
/// it is the first thing that channel has ever said. It is the digest's first-entry rule
/// (<see cref="WakeUp_Policy.Contains_DigestableTraffic"/>): a spoke appears when a member is
/// created, its first entry is that member's boot greeting, and holding a greeting costs a whole
/// window before the new member can be briefed. Answered by
/// <see cref="WakeDecision_Resolver.Nothing_EverDelivered"/> — read from the CURSOR and never from
/// the entry's <c>[n]</c>, which is agent-written (CLAUDE.md decision 12).
/// </param>
public readonly record struct SourceRead(ITurnSource Source, IReadOnlyList<IChannelEntry> Pending, bool NothingEverDelivered);
