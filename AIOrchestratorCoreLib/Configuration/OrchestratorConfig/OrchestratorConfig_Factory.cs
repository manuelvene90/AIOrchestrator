using AIOrchestratorCoreLib.Configuration.RepoEntry;

namespace AIOrchestratorCoreLib.Configuration.OrchestratorConfig;

public static class OrchestratorConfig_Factory
{
    /// <summary>The general supervisor's default model — routing "work on X" needs no expensive model.</summary>
    public const string DEFAULT_GENERAL_SUPERVISOR_MODEL = "sonnet";

    public static IOrchestratorConfig Create(
        IReadOnlyList<IRepoEntry> repos,
        string? supervisorModel,
        string? implementerModel,
        string? generalSupervisorModel,
        long? telegramSupergroupChatId,
        long? telegramOwnerUserId,
        string? telegramBotToken)
    {
        return new OrchestratorConfigModel(
            repos,
            supervisorModel,
            implementerModel,
            generalSupervisorModel ?? DEFAULT_GENERAL_SUPERVISOR_MODEL,
            telegramSupergroupChatId,
            telegramOwnerUserId,
            telegramBotToken);
    }

    public static IOrchestratorConfig Create_Empty()
    {
        return Create([], null, null, null, null, null, null);
    }
}
