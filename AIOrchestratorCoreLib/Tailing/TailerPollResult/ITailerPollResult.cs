using AIOrchestratorCoreLib.Tailing.CompletedChannelAppend;

namespace AIOrchestratorCoreLib.Tailing.TailerPollResult;

/// <summary>Outcome of one tailer poll across all discovered channels.</summary>
public interface ITailerPollResult
{
    IReadOnlyList<ICompletedChannelAppend> CompletedAppends { get; }

    /// <summary>Files whose length shrank since the last poll — an append-only protocol anomaly.</summary>
    IReadOnlyList<string> TruncatedFiles { get; }

    /// <summary>
    /// "&lt;path&gt; — &lt;error&gt;" for each channel this poll could not read. The tailer has no logger,
    /// and a channel that fails silently is a session the owner stops hearing from, so the failure
    /// travels back to the bridge to be logged.
    /// </summary>
    IReadOnlyList<string> UnreadableFiles { get; }

    /// <summary>
    /// Channels holding a COMPLETE trailing entry that cannot be emitted because the file does not
    /// end with a line break.
    ///
    /// <para>
    /// The last entry is only ever released by the quiet flush, and that flush requires a trailing
    /// newline — so a file whose final write omitted one keeps its last entry parsed, pending and
    /// invisible until somebody else appends a header. Nothing is lost; it is unbounded silence,
    /// which is worse, because the sender believes it was delivered.
    /// </para>
    /// <para>
    /// REPORTED RATHER THAN FLUSHED, deliberately. Content cannot distinguish "complete without a
    /// newline" from "still being written" — and neither can the newline itself, since a writer
    /// pausing after any completed line leaves a file ending in one mid-entry. The only discriminator
    /// is time, and if a longer window ever fired early the remainder would arrive with no header of
    /// its own, be read as noise and be DROPPED (see the no-header branch of Extract_CompleteEntries).
    /// A truncated entry PLUS a silently discarded tail is strictly worse than a delay, so the tailer
    /// says so and changes nothing.
    /// </para>
    /// </summary>
    IReadOnlyList<string> HeldTrailingEntryFiles { get; }
}
