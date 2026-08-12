using AIOrchestratorCoreLib.Sessions.OrchestrationMember;

namespace AIOrchestratorCoreLib.Sessions.OrchestrationSession;

/// <summary>The persisted state of one orchestration (session.json).</summary>
public interface IOrchestrationSession
{
    string OrchId { get; }
    string RepoName { get; }
    string RepoPath { get; }
    DateTime CreatedUtc { get; }
    long? TelegramTopicId { get; }

    /// <summary>
    /// The ONE status message in this orchestration's topic — posted once, edited forever.
    ///
    /// Persisted rather than held in memory precisely because a restart is when it matters: the
    /// narration canvas keeps its id in a field, which is why it cannot survive one. A second
    /// status message appearing after every restart is the bug this feature exists to avoid, so
    /// the id lives where the topic id lives.
    /// </summary>
    long? StatusLineMessageId { get; }

    /// <summary>
    /// The TRUE session-host shell pid, synced from the pid file after spawn — null while a spawn
    /// is in flight. Informational only: liveness is the watchdog's job (pid files), and agents
    /// must never infer death from this field.
    /// </summary>
    int? SupervisorPid { get; }

    /// <summary>Stamped on every supervisor spawn — the watchdog's grace window against double-spawn races.</summary>
    DateTime? SupervisorSpawnedUtc { get; }

    /// <summary>Same grace stamp for the communicator session (its pid lives only in its pid file).</summary>
    DateTime? CommunicatorSpawnedUtc { get; }

    /// <summary>Short human goal name (2-4 words) set by the supervisor once the goal is known; also the Telegram topic name.</summary>
    string? DisplayName { get; }

    /// <summary>Per-orchestration model overrides (owner: "use fable for this") — null = the config default.</summary>
    string? SupervisorModelOverride { get; }
    string? ImplementerModelOverride { get; }

    IReadOnlyList<IOrchestrationMember> Members { get; }

    /// <summary>
    /// This topic's own delivery mode, which OVERRIDES the app-wide setting when it is not Normal.
    /// Silenced = drop (the owner is reading this orchestration in its terminal); Deferred = keep
    /// and replay later (the owner is away). Inbound always works, whatever the mode.
    /// </summary>
    Telegram.TelegramDeliveryModes TelegramMode { get; }

    /// <summary>Set when the general supervisor closed this orchestration. Folder stays as audit trail.</summary>
    DateTime? ClosedUtc { get; }
}
