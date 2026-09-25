namespace AIOrchestratorCoreLib.GeneralSupervision.SpawnSiblingRequest;

/// <summary>
/// A request dropped by a SOLO asking for a SIBLING solo: a second orchestration, its own Telegram topic,
/// linked to the requester by an endeavour, for a parallel job the owner wants to steer separately
/// (spec 2026-09-23 §4.1). HELD until the owner taps (O1): it is a second session running indefinitely.
///
/// <para>
/// NO MODEL AND NO EFFORT, on purpose. The child copies the parent's implementer-slot overrides — the
/// owner's dial on this endeavour — so half of it cannot quietly run on a different one, and a session
/// can never pick the forbidden model here.
/// </para>
/// </summary>
public interface ISpawnSiblingRequest
{
    /// <summary>The requester's own orchestration.</summary>
    string OrchId { get; }

    /// <summary>The new topic's name, <c>&lt;code&gt; · &lt;2-4 words&gt;</c> — see <see cref="SiblingName_Rules"/>.</summary>
    string Name { get; }

    /// <summary>
    /// One line, at most <see cref="OrchestrationRequests_Reader.SIBLING_JOB_MAX_CHARS"/> characters. The
    /// owner sees it on the prompt and in the birth note, and it becomes the child's first FROM owner entry.
    /// </summary>
    string Job { get; }

    /// <summary>
    /// The <c>[n]</c> channel-append.sh printed when the requester appended its HANDOVER entry to its own
    /// outbox. The helper allocates it inside the lock, so it is the one index an agent does not guess
    /// (decision 12). Whether that entry exists is the executor's check, not the reader's.
    /// </summary>
    int HandoverIndex { get; }

    /// <summary>Absolute path of the child's git worktree, exactly as written. Validated by the executor.</summary>
    string WorktreePath { get; }

    /// <summary>Why one session is not enough. Mandatory and relayed to the owner.</summary>
    string Reason { get; }

    string SourceFilePath { get; }
}
