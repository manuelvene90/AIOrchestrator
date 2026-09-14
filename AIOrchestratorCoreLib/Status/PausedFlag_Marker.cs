using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Status;

/// <summary>
/// The on-disk face of PAUSE: a file a BASH LOOP can test for, exactly like
/// <see cref="MeetingFlag_Marker"/>.
///
/// Pause is the owner saying "I am done with this one for now, but do not close it". The app half of
/// that — held traffic, no nudges, no respawn — it can do on its own. The SESSION half it cannot: a
/// session with open ledger lines is refused a turn end by the run-to-the-end hook, which is a bash
/// script that cannot read session.json. Without this file a paused orchestration would be told to
/// keep working, which is the opposite of dormant.
///
/// <para>
/// IT MUST NOT OUTLIVE THE MODE, for the reason the meeting flag states: a file that lifts a guard
/// is a file that can quietly exempt a session forever. So it is DERIVED, never authored —
/// <see cref="Sync"/> runs for every session on the tick, so an app that died with a flag on disk
/// clears it the moment it returns and finds the session unpaused.
/// </para>
/// <para>
/// It is the one flag that must SURVIVE A RESPAWN, which is the opposite of the awaiting-answer flag
/// (that one is cleared for a dead session, because a new process never earned it). The asymmetry is
/// deliberate: awaiting-answer describes something a PROCESS was doing, and dies with it; pause
/// describes something the OWNER decided about the orchestration, and a respawned session must wake
/// up still paused rather than cheerfully resuming work the owner had stopped.
/// </para>
/// </summary>
public static class PausedFlag_Marker
{
    public const string FILE_NAME = ".paused";

    public static string Build_FilePath(ISupervisionPaths paths, string orchId)
    {
        return Path.Combine(paths.Get_OrchestrationFolder(orchId), FILE_NAME);
    }

    /// <summary>True when this orchestration is paused — the same question the hook asks from bash.</summary>
    public static bool Is_Paused(ISupervisionPaths paths, string orchId)
    {
        try
        {
            return File.Exists(Build_FilePath(paths, orchId));
        }
        catch
        {
            // Unreadable means unknown, and unknown must not pause anything: a pause that fails to
            // register costs the owner one more nudge, while a phantom pause costs them a session
            // that has silently stopped working with nothing on screen saying why.
            return false;
        }
    }

    /// <summary>
    /// Makes the file match the state. Returns whether anything changed, so the caller can log a
    /// transition without logging a no-op on every tick.
    /// </summary>
    public static bool Sync(ISupervisionPaths paths, string orchId, bool paused, out string? failure)
    {
        failure = null;
        var flagFile = Build_FilePath(paths, orchId);

        try
        {
            if (paused == File.Exists(flagFile))
                return false;

            if (paused)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(flagFile) ?? "");
                File.WriteAllText(flagFile, "This orchestration is paused by the owner. End your turn; nothing is expected of you until they lift it.\n");
            }
            else
            {
                File.Delete(flagFile);
            }

            return true;
        }
        catch (Exception exception)
        {
            // NEVER THROWS at the caller, and never fails SILENTLY either. The REMOVAL direction is
            // the dangerous one: a delete that fails leaves a session permanently exempt from the
            // turn-end hook, so it names the operation AND the path rather than saying "flag error".
            failure = $"could not {(paused ? "raise" : "clear")} the paused flag at '{flagFile}' — {exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }
}
