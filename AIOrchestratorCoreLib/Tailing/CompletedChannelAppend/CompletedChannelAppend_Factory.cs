using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Channels.DiscoveredChannel;

namespace AIOrchestratorCoreLib.Tailing.CompletedChannelAppend;

public static class CompletedChannelAppend_Factory
{
    public static ICompletedChannelAppend Create(
        IDiscoveredChannel channel,
        IReadOnlyList<IChannelEntry> entries)
    {
        if (entries.Count == 0)
            throw new ArgumentException($"A completed append must carry at least one entry (channel '{channel.FilePath}')");

        return new CompletedChannelAppendModel(channel, entries);
    }
}
