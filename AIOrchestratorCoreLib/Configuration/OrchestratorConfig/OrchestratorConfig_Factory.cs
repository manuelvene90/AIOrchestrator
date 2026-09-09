using AIOrchestratorCoreLib.Configuration.RepoEntry;

namespace AIOrchestratorCoreLib.Configuration.OrchestratorConfig;

public static class OrchestratorConfig_Factory
{
    /// <summary>
    /// Owner's model ladder: routing = cheap, supervision and implementation = Fable 5.1
    /// (owner directive 2026-09-09, replacing opus). Pinned to the full model id rather than the
    /// 'fable' alias, which follows whatever the latest Fable happens to be.
    /// </summary>
    public const string DEFAULT_GENERAL_SUPERVISOR_MODEL = "sonnet";
    public const string DEFAULT_SUPERVISOR_MODEL = "claude-fable-5-1";
    public const string DEFAULT_IMPLEMENTER_MODEL = "claude-fable-5-1";
    public const string DEFAULT_COMMUNICATOR_MODEL = "sonnet";
    public const bool DEFAULT_TELEGRAM_ITALIAN_LAYER = true;

    /// <summary>Opt-in: a screenshot raises a real window, so an absent key must read as OFF.</summary>
    public const bool DEFAULT_TELEGRAM_STATUS_SCREENSHOTS = false;

    public static IOrchestratorConfig Create(
        IReadOnlyList<IRepoEntry> repos,
        string? supervisorModel,
        string? implementerModel,
        string? generalSupervisorModel,
        string? communicatorModel,
        long? telegramSupergroupChatId,
        long? telegramOwnerUserId,
        string? telegramBotToken,
        bool? telegramItalianLayer,
        bool? telegramStatusScreenshots,
        string? voiceTranscribeCommand,
        long? orchestrationTokenBudget)
    {
        return new OrchestratorConfigModel(
            repos,
            supervisorModel ?? DEFAULT_SUPERVISOR_MODEL,
            implementerModel ?? DEFAULT_IMPLEMENTER_MODEL,
            generalSupervisorModel ?? DEFAULT_GENERAL_SUPERVISOR_MODEL,
            communicatorModel ?? DEFAULT_COMMUNICATOR_MODEL,
            telegramSupergroupChatId,
            telegramOwnerUserId,
            telegramBotToken,
            telegramItalianLayer ?? DEFAULT_TELEGRAM_ITALIAN_LAYER,
            telegramStatusScreenshots ?? DEFAULT_TELEGRAM_STATUS_SCREENSHOTS,
            voiceTranscribeCommand,
            orchestrationTokenBudget);
    }

    public static IOrchestratorConfig Create_Empty()
    {
        return Create([], null, null, null, null, null, null, null, null, null, null, null);
    }

    /// <summary>
    /// The same config with only the Italian layer changed — the app's status-bar toggle and the
    /// /italian command both flip it live, and neither should have to restate every other field.
    /// </summary>
    public static IOrchestratorConfig Create_WithItalianLayer(IOrchestratorConfig source, bool telegramItalianLayer)
    {
        return Create(
            source.Repos,
            source.SupervisorModel,
            source.ImplementerModel,
            source.GeneralSupervisorModel,
            source.CommunicatorModel,
            source.TelegramSupergroupChatId,
            source.TelegramOwnerUserId,
            source.TelegramBotToken,
            telegramItalianLayer,
            source.TelegramStatusScreenshots,
            source.VoiceTranscribeCommand,
            source.OrchestrationTokenBudget);
    }

    /// <summary>
    /// The same config with only the status-screenshot flag changed — the /screenshots command
    /// flips it live from the phone, and like the Italian toggle it must not have to restate every
    /// other field just to move one bool.
    /// </summary>
    public static IOrchestratorConfig Create_WithStatusScreenshots(IOrchestratorConfig source, bool telegramStatusScreenshots)
    {
        return Create(
            source.Repos,
            source.SupervisorModel,
            source.ImplementerModel,
            source.GeneralSupervisorModel,
            source.CommunicatorModel,
            source.TelegramSupergroupChatId,
            source.TelegramOwnerUserId,
            source.TelegramBotToken,
            source.TelegramItalianLayer,
            telegramStatusScreenshots,
            source.VoiceTranscribeCommand,
            source.OrchestrationTokenBudget);
    }
}
