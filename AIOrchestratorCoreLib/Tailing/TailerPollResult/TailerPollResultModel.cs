using AIOrchestratorCoreLib.Tailing.CompletedChannelAppend;

namespace AIOrchestratorCoreLib.Tailing.TailerPollResult;

internal sealed class TailerPollResultModel(
    IReadOnlyList<ICompletedChannelAppend> completedAppends,
    IReadOnlyList<string> truncatedFiles,
    IReadOnlyList<string> unreadableFiles) : ITailerPollResult
{
    public IReadOnlyList<ICompletedChannelAppend> CompletedAppends { get; } = completedAppends;
    public IReadOnlyList<string> TruncatedFiles { get; } = truncatedFiles;
    public IReadOnlyList<string> UnreadableFiles { get; } = unreadableFiles;
}
