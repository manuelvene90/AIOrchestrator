namespace AIOrchestratorCoreLib.Running.TurnCursor;

/// <summary>
/// HOW FAR ONE SOURCE HAS BEEN DELIVERED. One per <see cref="TurnSource.ITurnSource"/>, persisted in
/// the session's state file, so a bridge restart neither re-delivers an entry nor loses one.
///
/// <para>
/// IT IS A SET OF IDENTITIES, NOT A NUMBER, and that is the whole of the design. See
/// <see cref="Channels.ChannelEntry_Digest"/> for why an index and a position both fail here: the index
/// is agent-written and has duplicated in production (CLAUDE.md decision 12), the position is not
/// monotonic because compaction moves entries out of the live file (decision 13). An entry is pending
/// iff its identity is not in <see cref="Delivered"/> — a rule that needs neither of them.
/// </para>
/// <para>
/// BOUNDED BY THE LIVE FILE, not by a cap. <see cref="Delivered"/> is pruned at every advance to the
/// identities still present in the live channel, so entries compaction has archived drop out and the
/// set can never outgrow <see cref="Channels.Channel_Compactor.COMPACT_ABOVE_ENTRIES"/> live entries.
/// An archived entry cannot come back — the compactor only ever moves from the front, and nothing
/// merges an archive back — so dropping its identity cannot cause a re-delivery.
/// </para>
/// </summary>
public interface ITurnCursor
{
    string SourceKey { get; }

    /// <summary>
    /// The file these identities were read from. Carried so the state file can be read on its own and
    /// so a source whose path changed is visible rather than silently re-baselined.
    /// </summary>
    string ChannelFilePath { get; }

    /// <summary>
    /// The highest <c>[n]</c> ever delivered from this source. DIAGNOSTIC ONLY — nothing decides
    /// delivery from it, and it must not start to: it is the untrusted field this cursor exists to stop
    /// depending on. It earns its place by making one silent hole audible — if the lowest index still
    /// live is above this, entries were archived without ever being handed over, and the dispatcher says
    /// so instead of the gap passing unremarked.
    /// </summary>
    int HighWaterIndex { get; }

    /// <summary>Identities of the inbound entries already handed to this session and still live.</summary>
    IReadOnlySet<string> Delivered { get; }
}
