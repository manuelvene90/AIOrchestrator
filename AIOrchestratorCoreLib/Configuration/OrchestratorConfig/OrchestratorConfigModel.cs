using AIOrchestratorCoreLib.Configuration.DefaultsSettings;
using AIOrchestratorCoreLib.Configuration.GuardrailSettings;
using AIOrchestratorCoreLib.Configuration.RepoEntry;
using AIOrchestratorCoreLib.Configuration.TelegramProseSettings;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.RunnerConfigs;

namespace AIOrchestratorCoreLib.Configuration.OrchestratorConfig;

internal sealed class OrchestratorConfigModel(
    IReadOnlyList<IRepoEntry> repos,
    string? supervisorModel,
    string? implementerModel,
    string? reviewerModel,
    string? soloModel,
    string? generalSupervisorModel,
    string? communicatorModel,
    long? telegramSupergroupChatId,
    long? telegramOwnerUserId,
    string? telegramBotToken,
    bool telegramStatusScreenshots,
    string? voiceTranscribeCommand,
    long? orchestrationTokenBudget,
    IRunnerConfigs runners,
    PlanBackendSettings? planBackend,
    IGuardrailSettings guardrails,
    IDefaultsSettings defaults,
    ITelegramProseSettings telegramProse,
    Telegram.TelegramInboundModes telegramInbound) : IOrchestratorConfig
{
    public IReadOnlyList<IRepoEntry> Repos { get; } = repos;
    public string? SupervisorModel { get; } = supervisorModel;
    public string? ImplementerModel { get; } = implementerModel;
    public string? ReviewerModel { get; } = reviewerModel;
    public string? SoloModel { get; } = soloModel;
    public string? GeneralSupervisorModel { get; } = generalSupervisorModel;
    public string? CommunicatorModel { get; } = communicatorModel;
    public long? TelegramSupergroupChatId { get; } = telegramSupergroupChatId;
    public long? TelegramOwnerUserId { get; } = telegramOwnerUserId;
    public string? TelegramBotToken { get; } = telegramBotToken;
    public bool TelegramStatusScreenshots { get; } = telegramStatusScreenshots;
    public string? VoiceTranscribeCommand { get; } = voiceTranscribeCommand;
    public long? OrchestrationTokenBudget { get; } = orchestrationTokenBudget;
    public IRunnerConfigs Runners { get; } = runners;
    public PlanBackendSettings? PlanBackend { get; } = planBackend;
    public IGuardrailSettings Guardrails { get; } = guardrails;
    public IDefaultsSettings Defaults { get; } = defaults;
    public ITelegramProseSettings TelegramProse { get; } = telegramProse;
    public Telegram.TelegramInboundModes TelegramInbound { get; } = telegramInbound;

    /// <summary>
    /// A SWITCH RATHER THAN A DICTIONARY, so the compiler is the thing that notices a new role: an
    /// unhandled one throws naming the value, which is how every other role map in the CoreLib
    /// behaves (<c>SessionRole_Names</c>, <c>ChannelAuthor_Words</c>). A default returned for an
    /// unknown role would spawn a session on a model nobody chose.
    /// </summary>
    public string? Get_ModelForRole(SessionRoles role)
    {
        return role switch
        {
            SessionRoles.Supervisor => SupervisorModel,
            SessionRoles.Implementer => ImplementerModel,
            SessionRoles.Reviewer => ReviewerModel,
            SessionRoles.Solo => SoloModel,
            SessionRoles.General => GeneralSupervisorModel,
            SessionRoles.Communicator => CommunicatorModel,
            _ => throw new Exception($"Unhandled SessionRoles: {role} — no model default is configured for it"),
        };
    }

    public bool Is_TelegramConfigured()
    {
        return TelegramSupergroupChatId != null
            && TelegramOwnerUserId != null
            && !string.IsNullOrWhiteSpace(TelegramBotToken);
    }
}
