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
    long? TelegramSupergroupChatId { get; }
    long? TelegramOwnerUserId { get; }
    string? TelegramBotToken { get; }

    bool Is_TelegramConfigured();
}
