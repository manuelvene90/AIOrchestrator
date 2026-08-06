using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Channels.DiscoveredChannel;

namespace AIOrchestratorCoreLib.Tailing.CompletedChannelAppend;

/// <summary>Complete new entries detected on one channel file during a tailer poll.</summary>
public interface ICompletedChannelAppend
{
    IDiscoveredChannel Channel { get; }
    IReadOnlyList<IChannelEntry> Entries { get; }
}
