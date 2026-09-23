using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// THE <c>/screens</c> TOGGLE'S SAVE, AND WHAT IT SAYS ABOUT IT — the app-wide switch for the periodic status's
/// screenshots, persisted in config.json because the owner asked for it to "work app wise, independently from where
/// I place the command". Moved out of <c>BridgeEngineModel</c> (its <c>Set_StatusScreenshots</c> and the reply
/// wording of <c>Toggle_StatusScreenshots_Async</c>) when plan 04 Task 2c had to touch it, by the rule that a piece
/// of that file a stage touches leaves it. The engine keeps only the Telegram half: the topic-name sync and the send.
///
/// <para>
/// A SAVE THAT IS REFUSED IS ANSWERED, NOT THROWN (ruling P35, 2026-09-23). <see cref="OrchestratorConfig_Loader.Save"/>
/// now throws an <see cref="IOException"/> rather than rewrite a config.json it could not open. Before that, a held
/// file was read as empty, the rename onto it failed on Windows, and the throw escaped the command: the update
/// handler logged "Handling update … failed" and the owner, who had just typed <c>/screens</c>, heard nothing — or,
/// on Linux, where the rename succeeds, had config.json replaced by Save's own keys. The owner CAUSED this one by
/// typing, so decision 15 lets the refusal reach the topic they typed in; the log gets one warning line with the
/// same reason. Only the two exception types a file save can raise are caught: anything else is a defect, and the
/// engine's per-update catch still logs it as one.
/// </para>
/// </summary>
public static class StatusScreenshots_Writer
{
    /// <summary>The machine-wide log line — the switch belongs to no orchestration.</summary>
    const string GLOBAL_ORCH_ID = "";

    /// <summary>
    /// Flips the flag <paramref name="current"/> carries and saves it. <c>Saved</c> is false when config.json refused
    /// the save; then NOTHING changed — the file is byte for byte, the setting reads as before — and
    /// <c>OwnerReply</c> says so, with the loader's own reason.
    /// </summary>
    public static (bool Saved, string OwnerReply) Toggle_Flag(
        IOrchestratorConfig current, ISupervisionPaths paths, IOrchestrationLog log)
    {
        var enabled = !current.TelegramStatusScreenshots;

        try
        {
            OrchestratorConfig_Loader.Save(OrchestratorConfig_Factory.Create_WithStatusScreenshots(current, enabled), paths);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Log_Warning(GLOBAL_ORCH_ID, Describe_NotSaved_ForLog(enabled, ex.Message));

            return (false, Describe_NotSaved_ForOwner(enabled, Shorten_Paths(ex.Message, paths)));
        }

        log.Log_Info(GLOBAL_ORCH_ID, enabled
            ? "Status screenshots ON — the periodic status carries a picture of each session's terminal"
            : "Status screenshots OFF — the periodic status is text only");

        return (true, enabled
            ? "📸 Status screenshots ON — every half-hourly status carries a picture of the session's terminal, taken only while you are away from the PC."
            : "📸 Status screenshots OFF — the half-hourly status is text only from here on.");
    }

    /// <summary>
    /// Names the file, never where it lives (ruling P36): the loader's reason carries the full local path, which is
    /// noise in a chat and says more about the machine than the owner needs there. The log line keeps the full path —
    /// that is where someone goes to find the file. Save reads secrets.json too, so the reply cannot simply say
    /// "config.json": the loader's reason says which of the two refused, and this keeps that, minus the folder.
    /// </summary>
    static string Shorten_Paths(string reason, ISupervisionPaths paths)
    {
        return reason
            .Replace(paths.ConfigFile, Path.GetFileName(paths.ConfigFile), StringComparison.Ordinal)
            .Replace(paths.SecretsFile, Path.GetFileName(paths.SecretsFile), StringComparison.Ordinal);
    }

    static string Describe_NotSaved_ForOwner(bool enabled, string ownerReason)
    {
        return $"📸 Status screenshots are still {Describe_State(!enabled)} — turning them {Describe_State(enabled)} could not be "
            + $"saved, so nothing changed. {ownerReason}";
    }

    static string Describe_NotSaved_ForLog(bool enabled, string reason)
    {
        return $"/screens could not turn status screenshots {Describe_State(enabled)} — config.json was not saved: {reason}";
    }

    static string Describe_State(bool enabled)
    {
        return enabled ? "ON" : "OFF";
    }
}
