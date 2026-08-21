using AIOrchestratorCoreLib.Status.SessionContextUsage;

namespace AIOrchestratorCoreLib.Formatting;

/// <summary>
/// The ONE wording for a context-window reading, so the status line, the half-hourly digest, the
/// /context report and the session's own terminal all say the same thing about the same number.
/// Item 12 of the project decisions: never a second copy of a formatter.
///
/// It TRUNCATES rather than rounds, which is the rule everywhere in this repo — 89.7% must read as
/// 89% and never as 90%, because 90 is a threshold the owner has attached meaning to.
/// </summary>
public static class ContextUsage_Formatter
{
    /// <summary>
    /// "ctx 93%". The word is spelled short deliberately: this field is appended to rows that are
    /// already at their wrap budget on a phone, and "context" costs four more characters on every
    /// member row of every topic.
    /// </summary>
    public static string Describe(ISessionContextUsage usage)
    {
        return $"ctx {(int)usage.UsedPercent}%";
    }

    /// <summary>
    /// The same, or null when there is no reading — for the surfaces that DROP the field rather
    /// than print an empty one. A dangling separator reads as a value that failed to load, when the
    /// truth is that the session has not reported one.
    /// </summary>
    public static string? Describe_OrNull(ISessionContextUsage? usage)
    {
        if (usage == null)
            return null;

        return Describe(usage);
    }
}
