using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Storage;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Bridge.EngineState;

/// <summary>
/// One JSON file, written through <see cref="Atomic_FileWriter"/>.
///
/// <para>
/// ATOMIC BECAUSE OF WHAT IS IN IT. A truncate-then-write that is interrupted mid-flight leaves a
/// zero-length file, and a zero-length file here reads as "no decisions pending" — every open
/// question on the owner's phone silently unanswerable, with the channel files on disk still intact
/// and nothing anywhere saying what happened. The rename cannot produce that state: the target is
/// either the old snapshot or the new one.
/// </para>
/// <para>
/// A DAMAGED FILE IS MOVED ASIDE AND REPORTED, never silently replaced — the same treatment
/// <see cref="BridgeState_Store"/> gives its cursor, so a human who goes looking finds the evidence
/// rather than an empty file where the evidence was.
/// </para>
/// </summary>
internal sealed class FileEngineStateStoreModel(ISupervisionPaths paths, IOrchestrationLog? log) : IEngineStateStore
{
    /// <summary>Orch id used for app-global log entries — the contract IOrchestrationLogEntry states.</summary>
    const string GLOBAL_ORCH_ID = "";

    readonly object _lock = new();

    public EngineStateSnapshot Load_OrEmpty()
    {
        lock (_lock)
        {
            if (!File.Exists(paths.EngineStateFile))
            {
                // NOT reported. Unlike the bridge cursor — whose absence means traffic was silently
                // never mirrored — an absent decision file on a first run means exactly what it
                // says: no decisions are outstanding. Warning here would fire on every clean start.
                return EngineStateSnapshot.Empty;
            }

            string text;

            try
            {
                text = File.ReadAllText(paths.EngineStateFile);
            }
            catch (Exception readException)
            {
                // Broad by intent: locked, denied, unreadable sector — the cause changes nothing
                // here, and the file may well be intact, so it is NOT quarantined.
                log?.Log_Warning(GLOBAL_ORCH_ID, Describe_EmptyState($"could not be read ({readException.Message})"));
                return EngineStateSnapshot.Empty;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                log?.Log_Warning(GLOBAL_ORCH_ID, Describe_EmptyState("is empty"));
                return EngineStateSnapshot.Empty;
            }

            var (snapshot, dropped) = EngineState_Serializer.Parse(text);

            if (dropped > 0)
            {
                // SAID, NOT SWALLOWED. Records are dropped one at a time and the rest of the file
                // is kept, so this is not a corruption worth quarantining — but a decision that
                // vanished must never do so quietly.
                log?.Log_Warning(
                    GLOBAL_ORCH_ID,
                    $"Bridge decision state '{paths.EngineStateFile}' had {dropped} unreadable record(s) — they were DROPPED; any question they held is answerable by typing, not by tapping");
            }

            return snapshot;
        }
    }

    public void Save(EngineStateSnapshot snapshot)
    {
        lock (_lock)
        {
            try
            {
                Atomic_FileWriter.Write_AllText(paths.EngineStateFile, EngineState_Serializer.To_Json(snapshot));
            }
            catch (Exception writeException)
            {
                // Broad by intent, and it must NOT propagate: everything in the snapshot is still
                // live in the engine's own fields, so this process keeps working exactly as before.
                // What is lost is only the ability to survive a crash that has not happened.
                log?.Log_Warning(
                    GLOBAL_ORCH_ID,
                    $"Bridge decision state could not be saved to '{paths.EngineStateFile}' ({writeException.Message}) — this process is unaffected, but a restart would forget the decisions currently pending");
            }
        }
    }

    string Describe_EmptyState(string reason)
    {
        return $"Bridge decision state '{paths.EngineStateFile}' {reason} — starting with NO pending decisions: any question already on the owner's phone can still be answered by typing, but its buttons are dead";
    }
}
