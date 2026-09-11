using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Storage;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Limits;

/// <summary>
/// A HEADLESS SESSION'S LIMITS, IN THE SHAPE THE APP ALREADY READS. Every usage figure in this app
/// comes from the status line, which a <c>-p</c> session never renders — measured, and the reason
/// the print runner contributes nothing to /limits, to the 90/95/…/100 % alerts, or to the dispatch
/// pause. The stream carries them instead, as a top-level <c>rate_limit_event</c> whose
/// <c>rate_limit_info.unifiedWindows</c> holds the same two windows in FRACTIONS
/// (<c>five_hour.utilization 0.73</c>) with unix <c>resetsAt</c> instants.
///
/// <para>
/// SO THIS TRANSLATES, IT DOES NOT ADD A SECOND PATH. The event becomes exactly the status line's
/// <c>rate_limits</c> object and is written to a <c>*usage.json</c> under the supervision root,
/// which is where <see cref="RateLimits_Reader.Find_UsageFiles_WithLiveWindow"/> already looks. Both
/// readers then see it — the strict one behind /limits and the tolerant
/// <see cref="LimitData_Parser"/> behind the alerts and the pause — with no branch, no new consumer
/// and no second opinion about what the account's usage is. CLAUDE.md's rule that usage figures
/// have ONE reader is the whole argument for doing it this way rather than teaching four call sites
/// about a new event.
/// </para>
/// <para>
/// The file carries NO cost or token fields, deliberately. <c>UsageTotals_Reader</c> composes its
/// per-source paths from the roster and never discovers this one, so it cannot double-count; and if
/// it ever did, a file with no money in it adds none.
/// </para>
/// </summary>
public static class RateLimitEvent_Translator
{
    public const string FILE_SUFFIX = ".limits.usage.json";
    public const string UNIFIED_WINDOWS_KEY = "unifiedWindows";
    public const string UTILIZATION_KEY = "utilization";
    public const string RESETS_AT_KEY = "resetsAt";
    public const string RATE_LIMITS_KEY = "rate_limits";
    public const string USED_PERCENTAGE_KEY = "used_percentage";
    public const string STATUS_LINE_RESETS_AT_KEY = "resets_at";

    /// <summary>Beside the session's own state file, so it is removed with the session's folder and nothing else.</summary>
    public static string Get_UsageFile(ISupervisionPaths paths, SessionRoles role, string orchId, string memberId)
    {
        var stateFile = PrintSessionState_Store.Get_StateFile(paths, role, orchId, memberId);
        var folder = Path.GetDirectoryName(stateFile) ?? paths.Root;
        var prefix = Path.GetFileName(stateFile).Replace(PrintSessionState_Store.STATE_FILE_NAME, string.Empty);

        return Path.Combine(folder, prefix.TrimEnd('.') + FILE_SUFFIX);
    }

    /// <summary>
    /// The status line's shape, built from the event. Returns null when the event carries no window
    /// this app can place in time — an unreadable reading is worse than none, because both "is this
    /// newer" and "has it already reset" become unanswerable (the rule
    /// <see cref="LimitData_Parser"/> states for the same reason).
    /// </summary>
    public static JsonObject? Build_StatuslinePayload_OrNull(JsonObject rateLimitInfo, string? modelName)
    {
        if (rateLimitInfo[UNIFIED_WINDOWS_KEY] is not JsonObject windows)
            return null;

        var rateLimits = new JsonObject();

        foreach (var pair in windows)
        {
            if (pair.Value is not JsonObject window)
                continue;

            var percent = Read_Percent_OrNull(window);

            if (percent == null)
                continue;

            var translated = new JsonObject { [USED_PERCENTAGE_KEY] = percent.Value };

            if (Read_ResetsAt_OrNull(window) is long resetsAt)
                translated[STATUS_LINE_RESETS_AT_KEY] = resetsAt;

            rateLimits[pair.Key] = translated;
        }

        if (rateLimits.Count == 0)
            return null;

        var payload = new JsonObject { [RATE_LIMITS_KEY] = rateLimits };

        if (!string.IsNullOrWhiteSpace(modelName))
            payload["model"] = new JsonObject { ["display_name"] = modelName };

        return payload;
    }

    /// <summary>Writes the translated payload; false when the event said nothing readable, or the write failed.</summary>
    public static bool Write_UsageFile(ISupervisionPaths paths, SessionRoles role, string orchId, string memberId, JsonObject rateLimitInfo, string? modelName)
    {
        var payload = Build_StatuslinePayload_OrNull(rateLimitInfo, modelName);

        if (payload == null)
            return false;

        try
        {
            Atomic_FileWriter.Write_AllText(Get_UsageFile(paths, role, orchId, memberId), payload.ToJsonString(Configuration.JsonWriting.INDENTED));
            return true;
        }
        catch
        {
            // A limits reading that cannot be filed is not worth failing a turn over.
            return false;
        }
    }

    /// <summary>"5h 73%, weekly 21%" — for the log line that says a new reading arrived.</summary>
    public static string Describe(JsonObject rateLimitInfo)
    {
        var payload = Build_StatuslinePayload_OrNull(rateLimitInfo, null);

        if (payload == null)
            return "no readable window";

        List<string> parts = [];

        foreach (var window in RateLimits_Reader.Read_Windows(payload.ToJsonString()))
            parts.Add($"{window.Window} {window.Percent:0.#}%");

        return parts.Count == 0 ? "no readable window" : string.Join(", ", parts);
    }

    /// <summary>
    /// The event states a FRACTION (0.73). The status line states a percentage, and
    /// <see cref="LimitData_Parser"/> deliberately reads a value of exactly 1 as one percent — so a
    /// fraction has to be converted HERE, where the unit is known, rather than left to a reader
    /// whose whole design is not to assume one.
    /// </summary>
    static double? Read_Percent_OrNull(JsonObject window)
    {
        try
        {
            var utilization = window[UTILIZATION_KEY]?.GetValue<double>();

            if (utilization == null || utilization.Value < 0)
                return null;

            return Math.Round(utilization.Value * 100.0, 2);
        }
        catch
        {
            return null;
        }
    }

    static long? Read_ResetsAt_OrNull(JsonObject window)
    {
        try
        {
            return window[RESETS_AT_KEY]?.GetValue<long>();
        }
        catch
        {
            return null;
        }
    }
}
