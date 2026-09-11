using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running.TurnSource;

namespace AIOrchestratorCoreLib.Running.PendingTraffic;

/// <summary>
/// One entry waiting for a turn, WITH THE CHANNEL IT CAME FROM. The pair travels together from the
/// moment it is selected to the moment it is written into the prompt, because the session cannot answer
/// it without knowing where it came from — and the bridge cannot file the answer without knowing either.
/// </summary>
public readonly record struct PendingEntry(ITurnSource Source, IChannelEntry Entry);
