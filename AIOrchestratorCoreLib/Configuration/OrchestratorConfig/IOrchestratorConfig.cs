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

    /// <summary>
    /// The owner reads/writes Italian on Telegram while sessions and channels stay 100% English:
    /// inbound owner texts are translated to English before reaching a channel, outbound mirror
    /// texts to Italian before sending. Canned strings (ticks, thinking, limit alerts) stay English.
    /// </summary>
    bool TelegramItalianLayer { get; }

    bool Is_TelegramConfigured();
}
