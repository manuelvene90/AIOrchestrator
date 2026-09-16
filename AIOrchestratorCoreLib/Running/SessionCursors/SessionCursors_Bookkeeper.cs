using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.TurnCursor;
using AIOrchestratorCoreLib.Running.TurnSource;
using AIOrchestratorCoreLib.Running.WakeDecision;

namespace AIOrchestratorCoreLib.Running.SessionCursors;

/// <summary>
/// WHAT A SESSION HAS ALREADY BEEN HANDED, kept on its state file — the read that persists a cursor
/// the session has never had, and the advance that records a set of entries as delivered.
///
/// <para>
/// BOTH HALVES WERE PRIVATE TO <c>PrintTurnDispatcherModel</c> (<c>Read_Sources</c> and
/// <c>Advance_Cursors</c>), which was right while a state file only ever belonged to a session the
/// dispatcher ran. It does not any more: after the one-wake-model change (2026-09-15) a TERMINAL
/// session keeps the same file for its cursors, and the engine's wake-ticket sweep does the same two
/// pieces of bookkeeping for it. CLAUDE.md decision 12 — never a second copy — so the dispatcher
/// calls this too and there is one implementation of each.
/// </para>
/// <para>
/// THE PERSIST IS NOT OPTIONAL FOR A SESSION WITH NO CURSORS AT ALL, and that is the whole reason
/// this is shared rather than left where it was. <see cref="WakeDecision_Resolver.Read_Pending"/>
/// BASELINES a session it has never seen — everything now in the channel is history — and writes
/// nothing. A caller that merely asks and discards would baseline again on the next tick, over a
/// file that has meanwhile grown, and absorb as history the very entry it was supposed to wake on:
/// permanently deaf, silently.
/// </para>
/// </summary>
public static class SessionCursors_Bookkeeper
{
    static readonly StringComparer SOURCE_KEYS = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Reads every source through <see cref="WakeDecision_Resolver.Read_Pending"/> and PERSISTS the
    /// cursor set when the read made one — see the type header for why that write is load-bearing.
    ///
    /// <para>
    /// THE OBSERVER IS THE CALLER'S EXTRA WORK, handed the cursor and the file DURING the read, while
    /// they are still the pair the pending set was selected against. The dispatcher uses it to log a
    /// baselined channel and to warn about an archive gap; the sweep passes none.
    /// </para>
    /// </summary>
    public static IReadOnlyList<SourceRead> Read_AndPersist(
        string stateFile,
        ref IPrintSessionState state,
        SessionRoles role,
        IReadOnlyList<ITurnSource> sources,
        WakeDecision_Resolver.SourceReadObserver? observe = null)
    {
        List<ITurnCursor> cursors = [];
        var changed = false;

        var reads = WakeDecision_Resolver.Read_Pending(state, role, sources, (source, cursor, entries, cursorIsNew) =>
        {
            if (cursorIsNew)
                changed = true;

            cursors.Add(cursor);
            observe?.Invoke(source, cursor, entries, cursorIsNew);
        });

        // A CURSOR IS NEVER DROPPED FOR A SOURCE THAT MERELY DID NOT RESOLVE THIS TICK. It used to be,
        // to keep the file tidy — and the roster read that decides is best-effort: an absent session.json
        // resolves to the owner channel alone, and rewriting the durable record from that transient
        // answer loses every spoke cursor irreversibly. The next successful read then meets them all as
        // unknown keys and re-delivers whole channels. Keeping a stale key costs one line in a JSON file
        // nobody counts; dropping it costs a supervisor re-answering every member it has.
        foreach (var cursor in state.Cursors)
        {
            if (!sources.Any(source => SOURCE_KEYS.Equals(source.Key, cursor.SourceKey)))
                cursors.Add(cursor);
        }

        if (changed)
        {
            state = PrintSessionState_Factory.CreateFrom_Existing_Cursors(state, cursors);
            PrintSessionState_Store.Write(stateFile, state);
        }

        return reads;
    }

    /// <summary>
    /// The cursor set that records <paramref name="pending"/> as handed over. Returns it rather than
    /// writing it: the dispatcher folds it into the record of a completed turn, and the wake-ticket
    /// sweep writes it beside the ticket.
    /// </summary>
    public static IReadOnlyList<ITurnCursor> Advance(
        IPrintSessionState state,
        IReadOnlyList<ITurnSource> sources,
        IReadOnlyList<PendingEntry> pending,
        IReadOnlySet<string>? answeredSourceKeys = null)
    {
        var byKey = state.Cursors.ToDictionary(cursor => cursor.SourceKey, SOURCE_KEYS);

        List<ITurnCursor> advanced = [];

        foreach (var source in sources)
        {
            if (!byKey.TryGetValue(source.Key, out var cursor))
                continue;

            // THE CLOSING TURN'S EXCEPTION, and only its (see PrintTurnDispatcherModel's
            // Close_Down_KilledTurn_Async). Nothing is dropped here: the cursor is carried over
            // untouched, so the source keeps its history and its entries are pending again on the
            // next tick.
            if (answeredSourceKeys != null && !answeredSourceKeys.Contains(source.Key))
            {
                advanced.Add(cursor);
                continue;
            }

            var delivered = pending.Where(item => SOURCE_KEYS.Equals(item.Source.Key, source.Key)).Select(item => item.Entry).ToList();
            var entries = ChannelHistory_Cache.Read_Entries(source.ChannelFilePath);

            advanced.Add(TurnCursor_Factory.CreateFrom_Delivered(cursor, state.Role, entries, delivered));
        }

        return advanced;
    }
}
