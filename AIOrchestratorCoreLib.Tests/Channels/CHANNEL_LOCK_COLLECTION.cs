using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// Serialises every test class that exercises the channel lock.
/// <para>
/// WHAT IS LEFT OF THE REASON: these classes contend for real filesystem locks and spawn bash
/// processes, and several assert on a wait measured in hundreds of milliseconds. Serialising them
/// makes those timing assertions mean what they say.
/// </para>
/// <para>
/// THE SINK IS NO LONGER ONE OF THE REASONS (2026-09-09). This collection also used to be justified
/// by <c>ChannelLock_Diagnostics</c> being a process-wide sink, and that justification never held:
/// it serialises the classes IN it, while the theft came from the Bridge tests OUTSIDE it that start
/// a real engine — <c>BridgeEngineModel.Run_Async</c> rewires the sink at every start. Five of six
/// full runs on this branch were red for it. Diagnostics are now captured per async flow
/// (<c>ChannelLock_Diagnostics.Capture_OnThisFlow</c>), so a capture cannot be taken by any test in
/// or out of this collection, and no test has to join it to be safe.
/// </para>
/// </summary>
[CollectionDefinition(NAME)]
public class CHANNEL_LOCK_COLLECTION
{
    public const string NAME = "channel-lock";
}
