namespace AIOrchestratorCoreLib.Bridge.SuppressedEntries;

/// <summary>
/// THE ENTRIES <c>phone.push = filtered</c> HOLDS BACK, per orchestration, until the turn the owner
/// was waiting on ends — then handed over as ONE message (the turn-ended completion). Moved OUT of
/// <c>BridgeEngineModel</c> when the filter came back (plan 03 Task 2): master kept it there as
/// <c>_suppressedEntries</c>, and the fork deleted it with the filter on 2026-09-09.
///
/// <para>
/// A LIST, NOT A SLOT — and that is the whole point. Until 2026-09-10 it was ONE slot, overwritten by
/// each suppression: the real answer to the owner's question was filed there, a "WAITING ON …" status
/// line written a minute later replaced it, and the turn-ended receipt delivered the status line. The
/// owner re-typed their question three times that morning (decision 25).
/// </para>
/// <para>
/// CAPPED, OLDEST DROPPED, AND SAID ONCE. An orchestration that runs for a day with the owner away must
/// not grow a digest nobody can read; when something has to go, the newest words are the ones worth
/// keeping. The overflow is ONE Info line per fill, naming the first entry dropped — every entry is
/// still in the channel file and the app. Once, because entries filed outside a reply turn are never
/// drained, so an ordinary autonomous stretch keeps the list full for hours: a warning per entry past
/// the cap was a false alarm repeated for as long as the session worked (ruling R9). Drain and Forget
/// end the fill, so the next one is said again. The log, never the phone: the owner cannot act on it
/// (decision 15).
/// </para>
/// <para>
/// WHAT IT DOES NOT DO: release anything on its own. Master also released a stalled orchestration's
/// held words after five idle minutes (<c>Break_SilentDeadlock_Async</c>); that net does not exist on
/// this tree and restoring it is not this store's job (plan 03's PARKED list).
/// </para>
/// </summary>
public interface ISuppressedEntries
{
    /// <summary>The most one orchestration holds before the oldest is dropped.</summary>
    const int MAX_ENTRIES_PER_ORCHESTRATION = 20;

    /// <summary>
    /// Appends an entry to the back of this orchestration's list. <paramref name="text"/> is what the
    /// owner will read — already formatted, speaker glyph included; <paramref name="subject"/> names the
    /// entry in the log line if it is ever dropped.
    /// </summary>
    void File(string orchId, string? subject, string text);

    /// <summary>
    /// Everything held for this orchestration, in the order it was said, and REMOVED — a digest handed
    /// over once is spent. Empty when nothing was held.
    /// </summary>
    IReadOnlyList<(string? Subject, string Text)> Drain(string orchId);

    /// <summary>
    /// Discards what is held for this orchestration without handing it over: the orchestration closed,
    /// or the owner has just spoken and whatever was said before belongs to a conversation that has
    /// moved on.
    /// </summary>
    void Forget(string orchId);
}
