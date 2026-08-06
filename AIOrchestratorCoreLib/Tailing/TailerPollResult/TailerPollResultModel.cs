using AIOrchestratorCoreLib.Tailing.CompletedChannelAppend;

namespace AIOrchestratorCoreLib.Tailing.TailerPollResult;

internal sealed class TailerPollResultModel(
    IReadOnlyList<ICompletedChannelAppend> completedAppends,
    IReadOnlyList<string> truncatedFiles) : ITailerPollResult
{
    public IReadOnlyList<ICompletedChannelAppend> CompletedAppends { get; } = completedAppends;
    public IReadOnlyList<string> TruncatedFiles { get; } = truncatedFiles;
}
