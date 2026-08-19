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

    /// <summary>
    /// The owner has finished this endeavour but has NOT tested it yet, so it must not be closed.
    ///
    /// SEPARATE FROM <see cref="TelegramMode"/> on purpose, and this is the whole design. The owner
    /// asked for a state "identical to /mute but with a different icon" (2026-08-19), and a fourth
    /// delivery mode would mean re-deriving mute's behaviour at every branch that asks whether a
    /// message is DROPPED or merely queued — nine of them. As a flag beside the mode, /test IS mute:
    /// the behaviour is identical by construction rather than by careful enumeration, and only the
    /// glyph differs.
    ///
    /// PERSISTED, because it is a reminder. Their words: they mute a finished endeavour and then
    /// "remind myself that all the muted ones still need testing before I close them" — a note that
    /// vanished when the app restarted would not be one.
    /// </summary>
    bool AwaitingTest { get; }

    /// <summary>
    /// WHERE THE OWNER IS for this orchestration. TERMINAL means they are in its terminal: nothing
    /// is pushed to Telegram and — the half that matters — no question raises the awaiting-answer
    /// flag, so the supervisor never freezes waiting for a tap that is being typed at it instead.
    /// Persisted, because an app restart does not move the owner out of their chair.
    /// </summary>
    Telegram.OwnerPresenceModes OwnerPresence { get; }

    /// <summary>Set when the general supervisor closed this orchestration. Folder stays as audit trail.</summary>
    DateTime? ClosedUtc { get; }
}
