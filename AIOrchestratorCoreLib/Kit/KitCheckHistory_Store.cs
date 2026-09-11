using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Kit;

/// <summary>
/// REMEMBERS THE LAST KIT VERDICT ACROSS RESTARTS, so a check that FAILS and later PASSES can say
/// so — once — instead of leaving the failure as the last word anyone read.
///
/// <para>
/// WHY (VPS, 2026-09-07): a kit check failed at boot, wrote its refusal onto the general channel,
/// and the daemon was restarted with the kit fixed. The passing check logged one INFO line and
/// wrote nothing to the channel, so the newest thing the general supervisor could see there was
/// still the refusal. It went on reporting a blocker that had been cleared hours earlier — and a
/// supervisor sitting on a stale blocker is indistinguishable, from the owner's side, from a
/// supervisor that has stopped working.
/// </para>
/// <para>
/// One tiny file, and every failure to read or write it is swallowed: this exists to add a
/// sentence, never to be a reason a host cannot start.
/// </para>
/// </summary>
public static class KitCheckHistory_Store
{
    public const string FILE_NAME = "kit-check.json";
    public const string LAST_VERDICT_KEY = "lastVerdict";

    public static string Get_File(ISupervisionPaths paths)
    {
        return Path.Combine(paths.Root, FILE_NAME);
    }

    /// <summary>Null when nothing has ever been recorded, or when the record cannot be read — both mean "no failure to recover from".</summary>
    public static PluginVerdicts? Read_LastVerdict_OrNull(ISupervisionPaths paths)
    {
        try
        {
            var file = Get_File(paths);

            if (!File.Exists(file))
                return null;

            var word = (JsonNode.Parse(File.ReadAllText(file)) as JsonObject)?[LAST_VERDICT_KEY]?.GetValue<string>();

            return Enum.TryParse<PluginVerdicts>(word, out var verdict) ? verdict : null;
        }
        catch
        {
            return null;
        }
    }

    public static void Write_LastVerdict(ISupervisionPaths paths, PluginVerdicts verdict)
    {
        try
        {
            Directory.CreateDirectory(paths.Root);

            File.WriteAllText(Get_File(paths), new JsonObject { [LAST_VERDICT_KEY] = verdict.ToString() }.ToJsonString());
        }
        catch
        {
            // The cost of losing this is one un-said recovery sentence, never a startup.
        }
    }

    /// <summary>
    /// <see cref="PluginVerdicts.Unchecked"/> is not a failure — nothing was ever wrong, so there is
    /// nothing to announce recovering from.
    /// </summary>
    public static bool Is_Failure(PluginVerdicts? verdict)
    {
        return verdict != null && verdict != PluginVerdicts.Ok && verdict != PluginVerdicts.Unchecked;
    }
}
