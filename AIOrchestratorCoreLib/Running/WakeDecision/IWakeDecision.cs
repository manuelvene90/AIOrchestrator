using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.TurnSource;

namespace AIOrchestratorCoreLib.Running.WakeDecision;

/// <summary>
/// THE ANSWER TO "DOES THIS SESSION TAKE A TURN NOW, AND WITH WHAT?" — the question this system used
/// to answer twice: in C# for a bridge-driven session and, independently, in the bash monitor of
/// <c>kit/skills/*/reference/watcher.md</c> for a terminal one. The bash copy had no cursor, no digest
/// and no notion of an entry that should wake nobody, so every mitigation built during the 2026-09-08
/// token-efficiency work reached only half the machines (2026-09-15 one-wake-model spec §1).
///
/// <para>
/// A null decision is not an error: it is "not yet" — nothing pending, or a member's ordinary report
/// still inside its digest window.
/// </para>
/// </summary>
public interface IWakeDecision
{
    /// <summary>Why the turn is starting, in the words the log and the wake ticket carry.</summary>
    string Reason { get; }

    /// <summary>Everything the turn is handed, ordered, with the app's riding notes already in front.</summary>
    IReadOnlyList<PendingEntry> Pending { get; }

    /// <summary>Every channel this session is woken by — the cursors to advance once the turn is admitted.</summary>
    IReadOnlyList<ITurnSource> Sources { get; }
}
