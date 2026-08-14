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
/// ONE SET, ONE MEANING: `firstSighted` holds every channel whose CONTENTS have been read, by anyone.
/// It used to be three registrations — this pass and each sweep keeping its own — and every pair of
/// them left a window: a channel one had seen and another had not, where an offence arriving in
/// between was absorbed as history by whichever got there second, and could never be reported (the
/// entry is on disk, its writer believes it visible, and the memos never release).
/// </para>
/// <para>
/// A SINGLE REGISTRATION FORCES A SINGLE ABSORPTION, and that is why one set is correct rather than
/// merely tidier. Whoever takes first sight must record BOTH memos at that instant; if it recorded
/// only its own, the other consumer would either re-announce history or swallow a new offence. So
/// this returns both key sets together and the caller applies them together (rev-10 F1, and the
/// residual rev-9 named on top of it).
/// </para>
/// <para>
/// IT COMPOSES NO KEYS OF ITS OWN. Both come from the builders the sweeps use. A baseline keyed even
/// slightly differently would record keys that never match, and every offence would be reported for
/// ever — which would look exactly like the bug this closes (decision 12).
/// </para>
/// <para>
/// THE DECISION AND THE KEYS ARE HERE; APPLYING THEM IS THE ENGINE'S. That split is what makes the
/// case above expressible at all: a test hands this a channel already in `firstSighted` and asserts
/// nothing comes back, which is a state no test can reach through the tick — it needs a file to
/// appear between two points inside one tick.
/// </para>
/// </summary>
public static class ChannelBaseline_Pass
{
    public static IReadOnlyList<ChannelBaseline> Build_ForUnseenChannels(
        IReadOnlyList<IDiscoveredChannel> channels,
        IReadOnlySet<string> firstSighted)
    {
        List<ChannelBaseline> baselines = [];

        foreach (var channel in channels)
        {
            if (firstSighted.Contains(channel.FilePath))
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
