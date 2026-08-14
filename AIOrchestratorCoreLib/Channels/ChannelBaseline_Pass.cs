using AIOrchestratorCoreLib.Channels.DiscoveredChannel;
using AIOrchestratorCoreLib.Usage;

namespace AIOrchestratorCoreLib.Channels;

/// <summary>What one previously-unseen channel contributes to the two memos, keys already composed.</summary>
public readonly record struct ChannelBaseline(
    string ChannelFilePath,
    IReadOnlyList<string> MalformedKeys,
    IReadOnlyList<string> CrossingKeys);

/// <summary>
/// WHAT A CHANNEL ALREADY CONTAINED THE FIRST TIME ANYTHING SAW IT — the history both sweeps absorb
/// silently, computed for channels neither sweep has reached yet.
///
/// <para>
/// WHY IT EXISTS. Both sweeps run BELOW the DND gate, and orchestrations can still be CREATED under
/// DND, so a channel born during a mute was first seen at unmute with everything accumulated in it
/// absorbed as "history" and unreportable for ever (rev-6 F2). Running this above the gate takes that
/// first sight when the channel appears instead.
/// </para>
/// <para>
/// IT SKIPS A CHANNEL EITHER SWEEP HAS ALREADY SEEN, and that gate is the whole correctness of it.
/// The first version gated on a set of its own, which nothing else wrote — so a channel a SWEEP saw
/// first was still unseen to this pass, which reached it a tick later and filed everything that had
/// arrived in between as history. The entry is on disk, its writer believes it visible, and neither
/// the sweep (key already recorded) nor the pass (memo never releases) will ever report it. That is
/// the same class the pass was written to close, returning through the ORDERING rather than the gate
/// (rev-10 F1, 2026-08-14).
/// </para>
/// <para>
/// EITHER, NOT BOTH: the two sweeps walk `ChannelDiscovery.Find_ChannelFiles` separately, so a file
/// created between them sits in one set only. Skipping on either is still right, because the sweep
/// that has not seen it keeps its own first-sight branch — which sits ABOVE its no-offence skip
/// precisely so it fires on sight of the file rather than on its first offence.
/// </para>
/// <para>
/// IT COMPOSES NO KEYS OF ITS OWN. Both come from the builders the sweeps use. A baseline keyed even
/// slightly differently would record keys that never match, and every offence would be reported for
/// ever — which would look exactly like the bug this closes (decision 12).
/// </para>
/// <para>
/// THE DECISION AND THE KEYS ARE HERE; APPLYING THEM IS THE ENGINE'S. That split is what makes the
/// case above expressible at all: a test hands this a channel already in `shapeBaselined` and asserts
/// nothing comes back, which is a state no test can reach through the tick — it needs a file to
/// appear between two points inside one tick.
/// </para>
/// </summary>
public static class ChannelBaseline_Pass
{
    public static IReadOnlyList<ChannelBaseline> Build_ForUnseenChannels(
        IReadOnlyList<IDiscoveredChannel> channels,
        IReadOnlySet<string> shapeBaselined,
        IReadOnlySet<string> indexBaselined)
    {
        List<ChannelBaseline> baselines = [];

        foreach (var channel in channels)
        {
            if (shapeBaselined.Contains(channel.FilePath) || indexBaselined.Contains(channel.FilePath))
                continue;

            var liveText = UsageTotals_Reader.Read_Text_Safe(channel.FilePath);

            List<string> malformedKeys = [];

            foreach (var entry in ChannelShape_Validator.Find_MalformedHeaders(liveText))
                malformedKeys.Add(ChannelShape_Validator.Build_MemoKey(channel.FilePath, entry.Line));

            var crossings = ChannelIndexSequence_Screen.Find_Crossings(
                ChannelIndexSequence_Screen.Read_Headers(
                    UsageTotals_Reader.Read_Text_Safe(Channel_Compactor.Build_ArchiveFilePath(channel.FilePath)),
                    liveText));

            List<string> crossingKeys = [];

            foreach (var crossing in crossings)
                crossingKeys.Add(ChannelIndexSequence_Screen.Build_MemoKey(channel.FilePath, crossing));

            baselines.Add(new ChannelBaseline(channel.FilePath, malformedKeys, crossingKeys));
        }

        return baselines;
    }
}
