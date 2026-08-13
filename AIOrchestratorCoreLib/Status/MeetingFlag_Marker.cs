using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Status;

/// <summary>
/// The on-disk face of TERMINAL presence: a file a BASH LOOP can test for.
///
/// Presence itself lives on the session, but the thing that has to read it most often is the
/// supervisor's watcher — a shell `while true` loop that fingerprints a channel file. It cannot
/// reasonably parse session.json, and it must go SILENT during a meeting or its wake notifications
/// litter the terminal the owner is trying to talk in. So the app materialises the mode as a file,
/// the same shape as <see cref="GuardNotInForce_Marker"/> and the awaiting-answer flag, both of which
/// exist because something outside the app needed to test a condition from bash.
///
/// <para>
/// IT MUST NOT OUTLIVE THE MODE. A file that can silence a watcher is a file that can make an
/// orchestration permanently deaf, so it is DERIVED, never authored: <see cref="Sync"/> is called on
/// every presence change, on close, and for every session at startup — so an app that died with a
/// flag on disk removes it the moment it comes back and finds the session's presence is Remote.
/// The one thing it cannot survive is the app never running again, which is true of the whole system.
/// </para>
/// <para>
/// GENERAL IS THE EXCEPTION, deliberately: it has no session.json, so for it this file IS the state
/// rather than a projection of it. That is stated here because two sources of truth by role is the
/// kind of thing a reader must be told rather than discover.
/// </para>
/// </summary>
public static class MeetingFlag_Marker
{
    public const string FILE_NAME = ".meeting";

    public static string Build_FilePath(ISupervisionPaths paths, string orchId)
    {
        var folder = orchId == ChannelDiscovery.GENERAL_ORCH_ID
            ? paths.GeneralFolder
            : paths.Get_OrchestrationFolder(orchId);

        return Path.Combine(folder, FILE_NAME);
    }

    /// <summary>True when this orchestration is in a meeting — the same question the watcher asks.</summary>
    public static bool Is_InMeeting(ISupervisionPaths paths, string orchId)
    {
        try
        {
            return File.Exists(Build_FilePath(paths, orchId));
        }
        catch
        {
            // Unreadable means unknown, and unknown must not silence anything: a meeting that fails
            // to register costs a few notifications, while a phantom meeting costs every wake.
            return false;
        }
    }

    /// <summary>
    /// Makes the file match the mode. Returns whether anything changed, so the caller can log a
    /// transition without logging a no-op on every tick.
    /// </summary>
    public static bool Sync(ISupervisionPaths paths, string orchId, OwnerPresenceModes presence)
    {
        var flagFile = Build_FilePath(paths, orchId);
        var wanted = presence == OwnerPresenceModes.Terminal;

        try
        {
            if (wanted == File.Exists(flagFile))
                return false;

            if (wanted)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(flagFile) ?? "");
                File.WriteAllText(flagFile, "The owner is in this session's terminal. The watcher stays armed and silent until this file is gone.\n");
            }
            else
            {
                File.Delete(flagFile);
            }

            return true;
        }
        catch
        {
            // Never throws at the caller: this is a convenience for a shell loop, and failing to
            // write it must not take down a presence toggle or the tick that reconciles it.
            return false;
        }
    }
}
