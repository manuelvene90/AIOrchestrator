using AIOrchestratorCoreLib.Channels.DiscoveredChannel;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Channels;

/// <summary>
/// Finds every channel file under the supervision root. An orchestration is any subfolder
/// holding a session.json; its channels are owner-channel.md plus every imp-*/channel.md.
/// </summary>
public static class ChannelDiscovery
{
    const string IMPLEMENTER_FOLDER_PREFIX = "imp-";

    /// <summary>Reserved orchestration id of the always-on general supervisor.</summary>
    public const string GENERAL_ORCH_ID = "general";

    public static IReadOnlyList<IDiscoveredChannel> Find_ChannelFiles(ISupervisionPaths paths)
    {
        List<IDiscoveredChannel> channels = [];

        if (!Directory.Exists(paths.Root))
            return channels;

        if (File.Exists(paths.GeneralChannelFile))
            channels.Add(DiscoveredChannel_Factory.Create_ForOwner(GENERAL_ORCH_ID, paths.GeneralChannelFile));

        foreach (var orchFolder in Directory.EnumerateDirectories(paths.Root))
        {
            var orchId = Path.GetFileName(orchFolder);

            if (orchId == GENERAL_ORCH_ID)
                continue;

            if (!File.Exists(paths.Get_SessionFile(orchId)))
                continue;

            var ownerChannel = paths.Get_OwnerChannelFile(orchId);
            if (File.Exists(ownerChannel))
                channels.Add(DiscoveredChannel_Factory.Create_ForOwner(orchId, ownerChannel));

            foreach (var memberFolder in Directory.EnumerateDirectories(orchFolder))
            {
                var memberId = Path.GetFileName(memberFolder);

                if (!memberId.StartsWith(IMPLEMENTER_FOLDER_PREFIX, StringComparison.OrdinalIgnoreCase))
                    continue;

                var channelFile = paths.Get_ImplementerChannelFile(orchId, memberId);
                if (File.Exists(channelFile))
                    channels.Add(DiscoveredChannel_Factory.Create_ForImplementer(orchId, memberId, channelFile));
            }
        }

        return channels;
    }
}
