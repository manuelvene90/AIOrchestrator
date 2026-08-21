using AIOrchestratorCoreLib.Limits;
using AIOrchestratorCoreLib.Usage;

namespace AIOrchestratorCoreLib.Status.SessionContextUsage;

/// <summary>
/// Reads one session's context-window pressure out of the status-line probe file it writes beside
/// itself (.usage.json).
///
/// THE FIELD IS PARSED IN EXACTLY ONE PLACE — <see cref="RateLimits_Reader.Read_ContextPercent_OrNull"/>
/// — and this factory only decides whether there is a reading at all. That function sat in the repo
/// with zero call sites from the day it was written until 2026-08-21; wiring it was cheaper than a
/// second parser, and a second parser is how two surfaces start disagreeing about one number.
/// </summary>
public static class SessionContextUsage_Factory
{
    /// <summary>
    /// A reading from figures already in hand. The file-reading overload below is how the app gets
    /// one; this exists so a caller that already knows the numbers — a test, or any future source
    /// that is not a probe file — does not have to write a file to express one, and so that
    /// `new SessionContextUsageModel` stays inside this factory where the pattern requires it.
    /// </summary>
    public static ISessionContextUsage Create(double usedPercent, DateTime probeTimeUtc)
    {
        return new SessionContextUsageModel(usedPercent, probeTimeUtc);
    }

    /// <summary>
    /// The reading, or null when there is none: no probe file yet, an unreadable one, or a Claude
    /// Code version whose status line carries no `context_window` at all. Null means UNKNOWN and
    /// every surface drops the field for it — never 0%, which would read as an empty window.
    /// </summary>
    public static ISessionContextUsage? Create_OrNull(string usageFilePath)
    {
        try
        {
            if (!File.Exists(usageFilePath))
                return null;

            var rawJson = UsageTotals_Reader.Read_Text_Safe(usageFilePath);

            if (string.IsNullOrEmpty(rawJson))
                return null;

            var usedPercent = RateLimits_Reader.Read_ContextPercent_OrNull(rawJson);

            if (usedPercent == null)
                return null;

            return Create(usedPercent.Value, File.GetLastWriteTimeUtc(usageFilePath));
        }
        catch
        {
            return null;
        }
    }
}
