using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Channels.DiscoveredChannel;

namespace AIOrchestratorCoreLib.Tailing.CompletedChannelAppend;

internal sealed class CompletedChannelAppendModel(
    IDiscoveredChannel channel,
    IReadOnlyList<IChannelEntry> entries) : ICompletedChannelAppend
{
    public IDiscoveredChannel Channel { get; } = channel;
    public IReadOnlyList<IChannelEntry> Entries { get; } = entries;
}
