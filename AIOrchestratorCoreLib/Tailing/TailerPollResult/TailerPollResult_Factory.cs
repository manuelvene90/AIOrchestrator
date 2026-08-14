using AIOrchestratorCoreLib.Tailing.CompletedChannelAppend;

namespace AIOrchestratorCoreLib.Tailing.TailerPollResult;

public static class TailerPollResult_Factory
{
    public static ITailerPollResult Create(
        IReadOnlyList<ICompletedChannelAppend> completedAppends,
        IReadOnlyList<string> truncatedFiles)
    {
        return Create(completedAppends, truncatedFiles, [], []);
    }

    public static ITailerPollResult Create(
        IReadOnlyList<ICompletedChannelAppend> completedAppends,
        IReadOnlyList<string> truncatedFiles,
        IReadOnlyList<string> unreadableFiles)
    {
        return Create(completedAppends, truncatedFiles, unreadableFiles, []);
    }

    public static ITailerPollResult Create(
        IReadOnlyList<ICompletedChannelAppend> completedAppends,
        IReadOnlyList<string> truncatedFiles,
        IReadOnlyList<string> unreadableFiles,
        IReadOnlyList<string> heldTrailingEntryFiles)
    {
        return new TailerPollResultModel(completedAppends, truncatedFiles, unreadableFiles, heldTrailingEntryFiles);
    }
}
