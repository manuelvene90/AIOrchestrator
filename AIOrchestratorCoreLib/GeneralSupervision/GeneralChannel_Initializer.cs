using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.GeneralSupervision;

/// <summary>Ensures the general supervisor's home (folder + seeded channel + requests folder) exists.</summary>
public static class GeneralChannel_Initializer
{
    public static void Ensure_Exists(ISupervisionPaths paths)
    {
        Directory.CreateDirectory(paths.GeneralFolder);
        Directory.CreateDirectory(paths.RequestsFolder);

        if (!File.Exists(paths.GeneralChannelFile))
            File.WriteAllText(paths.GeneralChannelFile, ChannelSeed_Builder.Build_GeneralChannelSeed());
    }
}
