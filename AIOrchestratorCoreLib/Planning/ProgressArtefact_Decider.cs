namespace AIOrchestratorCoreLib.Planning;

/// <summary>What the app should do with an orchestration's progress artefact on this tick.</summary>
public enum ProgressArtefactActions
{
    /// <summary>Nothing has changed and the heartbeat is not due. The common case, and it costs nothing.</summary>
    None,

    /// <summary>Publish the current reading.</summary>
    Write,

    /// <summary>The ledger no longer parses: take the artefact away rather than leave a fossil.</summary>
    Delete,
}

/// <summary>
/// Write, delete, or do nothing — extracted from the engine for the same reason
/// <see cref="Telegram.TopicStatusLine_Decider"/> was: <c>BridgeEngineModel</c> is `internal sealed`
/// with no `InternalsVisibleTo`, so a rule decided inside it is not untested but UNREACHABLE, and
/// anything that can be a pure function must be one.
///
/// WHAT THIS TYPE CANNOT PIN, stated here so nobody reads its coverage as covering the feature: it
/// does not know WHERE it is called from. The call sits above the DND gate in the mirror tick on
/// purpose — below it, the owner's terminal would freeze the moment they pressed 🔕 — and that is a
/// property of call ORDER, which no function given text and timestamps can observe. The call site
/// carries a comment saying so.
/// </summary>
public static class ProgressArtefact_Decider
{
    /// <summary>
    /// How long an artefact may sit unrewritten before it is refreshed anyway.
    ///
    /// It is a HEARTBEAT, not a cache expiry, and the two halves of this feature are why. The tick is
    /// two seconds, so writing every tick is thirty disk writes a minute for a number that moves a few
    /// times an hour — but writing ONLY on change stops the file's timestamp advancing, and the status
    /// line treats an old file as absent. A correct-but-unchanged reading would then be discarded for
    /// looking stale. Rewriting once a minute keeps the timestamp meaning "the app is alive", so the
    /// renderer can apply its staleness rule without ever throwing away a good number.
    /// </summary>
    public const int HEARTBEAT_SECONDS = 60;

    /// <param name="currentJson">The reading to publish, or null when the ledger does not parse.</param>
    /// <param name="lastWrittenJson">What THIS PROCESS last wrote. In memory, so null after a restart.</param>
    /// <param name="artefactLastWrittenAt">The file's own timestamp, or null when it is not on disk.</param>
    public static ProgressArtefactActions Decide(
        string? currentJson,
        string? lastWrittenJson,
        DateTime? artefactLastWrittenAt,
        DateTime now)
    {
        if (currentJson == null)
        {
            // Nothing to remove is not a deletion. Asking for one every tick on an orchestration that
            // has never had a ledger would be a file system call per orchestration per two seconds,
            // for a file that has never existed.
            return artefactLastWrittenAt == null ? ProgressArtefactActions.None : ProgressArtefactActions.Delete;
        }

        // WHAT IS ON DISK IS A DIFFERENT QUESTION FROM WHAT THIS PROCESS REMEMBERS WRITING, and
        // treating them as one is the restart bug: after a restart the remembered text is null while
        // the file is present and current. That writes once on the first tick, which is right. The
        // reverse — remembering a write whose file has been deleted underneath us — must also write,
        // and it would not if the memory alone were consulted.
        if (artefactLastWrittenAt == null)
            return ProgressArtefactActions.Write;

        if (currentJson != lastWrittenJson)
            return ProgressArtefactActions.Write;

        // A FUTURE TIMESTAMP IS DUE, NOT FRESH. Subtraction gives a negative age, which compares as
        // "not old enough" and would suppress the heartbeat for as long as the clock stayed behind the
        // stamp — silently, and for a file that is by then the only thing the owner's terminal reads.
        // This repo has already paid for a future stamp read as a small positive number once.
        if (artefactLastWrittenAt > now)
            return ProgressArtefactActions.Write;

        return (now - artefactLastWrittenAt.Value).TotalSeconds >= HEARTBEAT_SECONDS
            ? ProgressArtefactActions.Write
            : ProgressArtefactActions.None;
    }
}
