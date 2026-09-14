using AIOrchestratorCoreLib.Limits;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Usage;

namespace AIOrchestratorCoreLib.Spawning;

/// <summary>
/// Answers ONE question before a supervisor or solo is (re)spawned: is there a conversation of this
/// slot's OWN that `claude --resume` can pick up? (Owner request 2026-09-10: a restarted solo or
/// supervisor should continue its conversation, not boot as a new session.)
///
/// The answer comes from the slot's probe file — the statusline dumps its raw payload there every
/// render, `session_id` and `transcript_path` included (decision 10) — and it is YES only when
/// the transcript the CLI itself named still exists and is not empty. `claude --resume` of an id
/// with no transcript prints "No conversation found with session ID" and exits (verified against
/// Claude Code 2.1.267); under the watchdog that would be a respawn loop, so such an id is never
/// named. The first spawn has no probe file at all, which is what makes the first spawn fresh
/// without anyone having to tell a first spawn from a respawn.
///
/// The id travels through a PowerShell command line and was written by a process the app does not
/// control, so only the CLI's own id shape — a UUID in 8-4-4-4-12 form — is ever handed on.
/// Reading goes through the shared probe reader, which strips the BOM every probe file carries.
/// </summary>
public static class ResumableSession_Resolver
{
    public static string? Resolve_ForSupervisor_OrNull(ISupervisionPaths paths, string orchId)
    {
        return Resolve_FromProbeFile_OrNull(Path.Combine(paths.Get_OrchestrationFolder(orchId), UsageTotals_Reader.SESSION_USAGE_FILE));
    }

    public static string? Resolve_ForMember_OrNull(ISupervisionPaths paths, string orchId, string memberId)
    {
        return Resolve_FromProbeFile_OrNull(Path.Combine(paths.Get_ImplementerFolder(orchId, memberId), UsageTotals_Reader.SESSION_USAGE_FILE));
    }

    public static string? Resolve_FromProbeFile_OrNull(string probeFilePath)
    {
        try
        {
            var rawStatuslineJson = UsageTotals_Reader.Read_Text_Safe(probeFilePath);

            if (string.IsNullOrWhiteSpace(rawStatuslineJson))
                return null;

            var sessionId = RateLimits_Reader.Read_SessionId_OrNull(rawStatuslineJson);

            if (sessionId == null || !Is_ClaudeSessionId(sessionId))
                return null;

            var transcriptPath = RateLimits_Reader.Read_TranscriptPath_OrNull(rawStatuslineJson);

            if (string.IsNullOrWhiteSpace(transcriptPath) || !Has_Content(transcriptPath))
                return null;

            return sessionId;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The CLI's own id shape, and nothing else — this string is quoted into a shell command.</summary>
    public static bool Is_ClaudeSessionId(string candidate)
    {
        return Guid.TryParseExact(candidate, "D", out _);
    }

    static bool Has_Content(string transcriptPath)
    {
        var transcript = new FileInfo(transcriptPath);

        return transcript.Exists && transcript.Length > 0;
    }
}
