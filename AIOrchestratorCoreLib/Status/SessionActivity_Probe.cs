using AIOrchestratorCoreLib.Limits;
using AIOrchestratorCoreLib.Usage;

namespace AIOrchestratorCoreLib.Status;

/// <summary>
/// Is a session working RIGHT NOW? Its status-line probe hands us the exact transcript path, and a
/// transcript growing in the last couple of minutes means a turn is in flight. This is the only
/// trustworthy liveness signal in the system — a session cannot observe its own dormancy, and the
/// channel markers only say what an agent DECLARED, not what it is doing.
/// </summary>
public static class SessionActivity_Probe
{
    /// <summary>Transcript freshness that counts as "working right now".</summary>
    public const int MIDTURN_SECONDS = 120;

    /// <summary>
    /// When this session last did ANYTHING, from its transcript's mtime. The only trustworthy
    /// liveness signal in the system: a session cannot observe its own dormancy, and its channel
    /// silence proves nothing because the protocol forbids acknowledgment-only entries — a live,
    /// obedient session with nothing to say writes nothing at all.
    /// </summary>
    public static DateTime? Get_LastActivityUtc_OrNull(string usageFilePath)
    {
        try
        {
            if (!File.Exists(usageFilePath))
                return null;

            var rawJson = UsageTotals_Reader.Read_Text_Safe(usageFilePath);
            var transcriptPath = RateLimits_Reader.Read_TranscriptPath_OrNull(rawJson);

            var probedFile = transcriptPath != null && File.Exists(transcriptPath) ? transcriptPath : usageFilePath;

            return File.GetLastWriteTimeUtc(probedFile);
        }
        catch
        {
            return null;
        }
    }

    public static bool Is_MidTurn(string usageFilePath)
    {
        try
        {
            if (!File.Exists(usageFilePath))
                return false;

            var rawJson = UsageTotals_Reader.Read_Text_Safe(usageFilePath);
            var transcriptPath = RateLimits_Reader.Read_TranscriptPath_OrNull(rawJson);

            // No transcript path (older Claude Code): the probe file's own mtime is the fallback,
            // since the status line rewrites it on every render.
            var probedFile = transcriptPath != null && File.Exists(transcriptPath) ? transcriptPath : usageFilePath;

            return (DateTime.UtcNow - File.GetLastWriteTimeUtc(probedFile)).TotalSeconds < MIDTURN_SECONDS;
        }
        catch
        {
            return false;
        }
    }
}
