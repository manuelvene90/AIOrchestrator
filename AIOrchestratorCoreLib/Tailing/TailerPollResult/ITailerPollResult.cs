using AIOrchestratorCoreLib.Tailing.CompletedChannelAppend;

namespace AIOrchestratorCoreLib.Tailing.TailerPollResult;

/// <summary>Outcome of one tailer poll across all discovered channels.</summary>
public interface ITailerPollResult
{
    IReadOnlyList<ICompletedChannelAppend> CompletedAppends { get; }

    /// <summary>Files whose length shrank since the last poll — an append-only protocol anomaly.</summary>
    IReadOnlyList<string> TruncatedFiles { get; }
}
