using AIOrchestratorCoreLib.Configuration.RepoEntry;

namespace AIOrchestratorCoreLib.Configuration.OrchestratorConfig;

/// <summary>
/// Orchestrator configuration, merged from config.json (non-secret) and secrets.json (bot token).
/// Telegram settings are optional: when absent the bridge runs file-only (no mirror, no remote input).
/// </summary>
public interface IOrchestratorConfig
{
    IReadOnlyList<IRepoEntry> Repos { get; }
    string? SupervisorModel { get; }
    string? ImplementerModel { get; }

    /// <summary>The general supervisor only routes "work on X" requests — a cheap model suffices (default: sonnet).</summary>
    string? GeneralSupervisorModel { get; }

    /// <summary>The per-orchestration press-secretary: narrates, never works — sonnet holds the boundary (default: sonnet).</summary>
    string? CommunicatorModel { get; }
    long? TelegramSupergroupChatId { get; }
    long? TelegramOwnerUserId { get; }
    string? TelegramBotToken { get; }

    /// <summary>
    /// The owner reads/writes Italian on Telegram while sessions and channels stay 100% English:
    /// inbound owner texts are translated to English before reaching a channel, outbound mirror
    /// texts to Italian before sending. Canned strings (ticks, thinking, limit alerts) stay English.
    /// </summary>
    bool TelegramItalianLayer { get; }

    /// <summary>
    /// External command that transcribes an owner voice note; {input} is replaced by the audio
    /// file path. Null = voice messages get a "not configured" reply. Example:
    /// "whisper {input} --model small --language it --output_format txt --output_dir -" style CLIs.
    /// </summary>
    string? VoiceTranscribeCommand { get; }

    /// <summary>
    /// Runaway guard: text the owner once an orchestration's lifetime tokens pass this ceiling.
    /// Null or 0 = no guard.
    /// </summary>
    long? OrchestrationTokenBudget { get; }

    bool Is_TelegramConfigured();
}
