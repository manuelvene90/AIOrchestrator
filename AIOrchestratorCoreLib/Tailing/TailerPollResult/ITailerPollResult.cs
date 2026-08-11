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
}
