using AIOrchestratorCoreLib.Configuration.RepoEntry;

namespace AIOrchestratorCoreLib.Configuration.OrchestratorConfig;

internal sealed class OrchestratorConfigModel(
    IReadOnlyList<IRepoEntry> repos,
    string? supervisorModel,
    string? implementerModel,
    string? generalSupervisorModel,
    long? telegramSupergroupChatId,
    long? telegramOwnerUserId,
    string? telegramBotToken) : IOrchestratorConfig
{
    public IReadOnlyList<IRepoEntry> Repos { get; } = repos;
    public string? SupervisorModel { get; } = supervisorModel;
    public string? ImplementerModel { get; } = implementerModel;
    public string? GeneralSupervisorModel { get; } = generalSupervisorModel;
    public long? TelegramSupergroupChatId { get; } = telegramSupergroupChatId;
    public long? TelegramOwnerUserId { get; } = telegramOwnerUserId;
    public string? TelegramBotToken { get; } = telegramBotToken;

    public bool Is_TelegramConfigured()
    {
        return TelegramSupergroupChatId != null
            && TelegramOwnerUserId != null
            && !string.IsNullOrWhiteSpace(TelegramBotToken);
    }
}
