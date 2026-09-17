using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Channels.StatusLog;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.TurnCursor;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.StatusNotes;

/// <summary>
/// THE STATUS LOG'S SIDE OF THE NOTE BOOKKEEPING — its entries, its cursor, and the advance that
/// records what rode a turn.
///
/// <para>
/// IT IS NOT AN <see cref="TurnSource.ITurnSource"/> AND MUST NOT BECOME ONE. A source is ADDRESSABLE:
/// <c>PrintTurnPrompt_Builder.Describe_Contract</c> lists them to the session as the channels it may
/// reply to, and <c>PrintTurnDispatcherModel</c>'s reply write checks a reply's address against them.
/// Nobody may reply to the status log, and listing it would put a fake channel in front of every
/// session for nothing. It gets a CURSOR, under <see cref="StatusLog_Store.CURSOR_KEY"/>, which is all
/// the delivery record it needs and which persists for free in the state file's existing
/// <c>cursors</c> array.
/// </para>
/// <para>
/// AND IT CANNOT START A TURN. <see cref="PrintTurn_Trigger.Is_Inbound"/> is false for
/// <see cref="ChannelAuthors.App"/> for every role — which is what every record here is, since
/// <see cref="StatusLog_Store"/> renders them <c>FROM app</c> — so nothing here could be pending even
/// if it were handed to <see cref="PrintTurn_Trigger.Select_Pending"/>, and it is not: the notes are
/// added by <c>WakeDecision_Resolver.With_AgentNotes</c> AFTER every wake-up rule has decided, which
/// is where that guarantee already lived and where it stays.
/// </para>
/// </summary>
public static class StatusNotes_Bookkeeper
{
    static readonly StringComparer SOURCE_KEYS = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// The log's records for this session, as channel entries, oldest first. Empty for a session whose
    /// role has never had its bookkeeping routed — which is every session on a machine that has not set
    /// the key, and which is why this is read unconditionally rather than behind the sink config: a
    /// machine that switches the key BACK must still deliver the notes already written.
    /// </summary>
    public static IReadOnlyList<IChannelEntry> Read_Entries(ISupervisionPaths paths, IPrintSessionState state)
    {
        return StatusLog_Store.Read_Entries(StatusLog_Store.Get_File(paths, state.Role, state.OrchId, state.MemberId));
    }

    public static ITurnCursor? Find_Cursor_OrNull(IPrintSessionState state)
    {
        return state.Cursors.FirstOrDefault(cursor => SOURCE_KEYS.Equals(cursor.SourceKey, StatusLog_Store.CURSOR_KEY));
    }

    /// <summary>
    /// Whether this entry has already been handed to the session, asked of the log's cursor. A session
    /// with no such cursor yet has been handed nothing from it — which is the right answer for a
    /// session that existed before the log did, and the reason this is not a baseline: baselining
    /// would absorb as history the very notes the first turn after the upgrade should carry.
    /// </summary>
    public static bool Is_Delivered(IPrintSessionState state, IChannelEntry entry)
    {
        return Find_Cursor_OrNull(state)?.Delivered.Contains(ChannelEntry_Digest.Compute(entry)) == true;
    }

    /// <summary>
    /// The log's cursor after <paramref name="handed"/> rode a turn, pruned to the records still in the
    /// file — the same prune <see cref="TurnCursor_Factory.CreateFrom_Delivered"/> performs for a
    /// channel, and it absorbs the log's trimming exactly as it absorbs compaction (decision 13).
    /// </summary>
    public static ITurnCursor Advance(
        ISupervisionPaths paths,
        IPrintSessionState state,
        IReadOnlyList<IChannelEntry> handed)
    {
        var logFile = StatusLog_Store.Get_File(paths, state.Role, state.OrchId, state.MemberId);
        var live = StatusLog_Store.Read_Entries(logFile);

        var cursor = Find_Cursor_OrNull(state)
            ?? TurnCursor_Factory.Create(StatusLog_Store.CURSOR_KEY, logFile, 0, new HashSet<string>());

        return TurnCursor_Factory.CreateFrom_Delivered(cursor, state.Role, live, handed);
    }
}
