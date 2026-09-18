namespace AIOrchestratorCoreLib.GeneralSupervision.ClearDispatchPauseRequest;

/// <summary>
/// An agent's request to lift the dispatch pause (<see cref="Limits.DispatchPause_Gate"/>). It is
/// OWNER-CONFIRMED, not owner-only: the agent may ask, and only a tap on the owner's phone grants it,
/// because the pause protects the bill. General-level, like set-telegram-muted — the pause is one
/// global flag, so there is no orchestration id to name.
///
/// <para>
/// Why it exists: 2026-09-11 and 2026-09-18, a pause decided on the previous account's reading held
/// for days, and the only lever was an operator with a shell — which no session can reach, by design
/// (NoNewPrivileges). docs/superpowers/specs/2026-09-11-the-brake-that-cannot-be-lifted.md §3.
/// </para>
/// </summary>
public interface IClearDispatchPauseRequest
{
    /// <summary>Who asked — a role word, so the owner's button can say who is waiting.</summary>
    string Requester { get; }

    /// <summary>Why, in the asker's words; the owner decides on this line.</summary>
    string Reason { get; }

    string SourceFilePath { get; }
}
