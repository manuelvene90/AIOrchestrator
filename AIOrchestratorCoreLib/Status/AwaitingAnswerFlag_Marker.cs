using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Status;

/// <summary>
/// The on-disk face of "this supervisor asked the owner something and must not do anything else":
/// a file the PreToolUse hook (<c>supervisor-awaiting-answer-check.sh</c>) tests for from bash, the
/// same shape and for the same reason as <see cref="MeetingFlag_Marker"/>.
///
/// <para>
/// It lives here rather than inside the engine because the RULE about it is what needed pinning.
/// The engine's only entry point is <c>Run_Async</c>, so a decision left in there is verified by a
/// timing-dependent loop test or by nothing at all — and it was nothing at all: `/pc` could stop a
/// NEW block from being raised while leaving an EXISTING one standing, with no test able to see the
/// difference. The path is built in one place so a second copy cannot drift from it.
/// </para>
/// </summary>
public static class AwaitingAnswerFlag_Marker
{
    public const string FILE_NAME = ".awaiting-answer";

    public static string Build_FilePath(ISupervisionPaths paths, string orchId)
    {
        return Path.Combine(paths.Get_OrchestrationFolder(orchId), FILE_NAME);
    }

    /// <summary>True when a question is with the owner and the supervisor's hook is refusing tool calls.</summary>
    public static bool Is_Raised(ISupervisionPaths paths, string orchId)
    {
        try
        {
            return File.Exists(Build_FilePath(paths, orchId));
        }
        catch
        {
            // Unreadable means unknown, and unknown must not invent a block.
            return false;
        }
    }

    /// <summary>
    /// What a PRESENCE CHANGE does to an already-raised block. Entering TERMINAL clears it; returning
    /// to REMOTE leaves it exactly as it was.
    /// <para>
    /// THIS IS THE FEATURE'S CENTRAL CASE, not an edge of it. The owner walks to the terminal
    /// BECAUSE the supervisor is already frozen waiting for a tap — so a `/pc` that only prevents the
    /// NEXT block leaves the current one standing for up to its ten-minute expiry, with the owner
    /// sitting in front of a session that will not answer them. Suppressing the raise was only half
    /// the mode (rev-4, 2026-08-13).
    /// </para>
    /// <para>
    /// Clearing on the way IN and not on the way OUT is deliberate: the question the flag stood for
    /// is answered in the terminal, in conversation, and there is no moment afterwards at which
    /// re-raising it would be right. Remote does not restore it because it never knew what it was.
    /// </para>
    /// </summary>
    public static bool Apply_Presence(ISupervisionPaths paths, string orchId, OwnerPresenceModes presence, out string? failure)
    {
        failure = null;

        if (presence != OwnerPresenceModes.Terminal)
            return false;

        return Clear(paths, orchId, out failure);
    }

    /// <summary>Raises it. Returns whether the file is now there.</summary>
    public static bool Raise(ISupervisionPaths paths, string orchId, out string? failure)
    {
        failure = null;

        try
        {
            var flagFile = Build_FilePath(paths, orchId);

            Directory.CreateDirectory(Path.GetDirectoryName(flagFile) ?? "");
            File.WriteAllText(flagFile, DateTime.UtcNow.ToString("O"));
            return true;
        }
        catch (Exception exception)
        {
            failure = $"could not raise the awaiting-answer flag — {exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    /// <summary>Removes it. Returns whether it removed anything, so a caller can log a transition only.</summary>
    public static bool Clear(ISupervisionPaths paths, string orchId, out string? failure)
    {
        failure = null;

        try
        {
            var flagFile = Build_FilePath(paths, orchId);

            if (!File.Exists(flagFile))
                return false;

            File.Delete(flagFile);
            return true;
        }
        catch (Exception exception)
        {
            // A delete that fails is the dangerous direction — it leaves a session blocked — so it is
            // reported rather than swallowed, and never thrown at a presence toggle.
            failure = $"could not clear the awaiting-answer flag — {exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }
}
