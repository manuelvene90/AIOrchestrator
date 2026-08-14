using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Storage;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// Persists bridge progress (.bridge-state.json): per-file mirror offsets and the last processed
/// Telegram update id, so an app restart neither re-mirrors old entries nor re-routes old messages.
/// <para>
/// STARTING WITHOUT THIS FILE IS NOT FREE, and an earlier version of this comment claimed it was —
/// "duplicate lines the owner can read past". It is the opposite: the tailer baselines a file it has
/// never seen at its CURRENT length, so everything appended between the last saved offset and this
/// start is never mirrored at all. A one-way hole, and a silent one, because the channel file on
/// disk stays intact. Inbound is nearly vacuous in the other direction — with the update id at 0,
/// Telegram re-serves only what it never had confirmed.
/// </para>
/// <para>
/// It is still the right trade against a load that THROWS: the only caller runs on the app's startup
/// path, so an exception there means the bridge is never constructed at all and the app comes up
/// permanently Telegram-blind, with no way back until a human finds and deletes the file. Losing
/// some recent traffic beats losing the owner's remote control — but it IS a loss, and every path
/// that produces an empty cursor now says so.
/// </para>
/// </summary>
public static class BridgeState_Store
{
    /// <summary>
    /// Orch id used for app-global log entries. This is the same empty-string id
    /// <c>BridgeEngineModel.GLOBAL_ORCH_ID</c> holds; that one is private to its own class, and
    /// <c>IOrchestrationLogEntry</c> documents "OrchId is empty for app-global events" as the contract.
    /// </summary>
    const string GLOBAL_ORCH_ID = "";

    const string CORRUPT_FILE_MARKER = "corrupt";
    const string QUARANTINE_STAMP_FORMAT = "yyyyMMdd-HHmmss";

    /// <summary>
    /// Reads the persisted cursor, or an empty one when there is nothing usable on disk.
    /// Corruption is handled but not reported anywhere — prefer the overload taking a log.
    /// </summary>
    public static (IReadOnlyDictionary<string, long> FileOffsets, long LastUpdateId) Load_OrEmpty(ISupervisionPaths paths)
    {
        return Load_OrEmpty(paths, log: null);
    }

    /// <summary>
    /// Reads the persisted cursor, or an empty one when there is nothing usable on disk. A damaged
    /// file is moved aside to a <c>.corrupt-yyyyMMdd-HHmmss.json</c> sibling (so it can be inspected
    /// and so the next save starts clean) and reported through <paramref name="log"/>. Never throws
    /// for the state of the file itself.
    /// </summary>
    public static (IReadOnlyDictionary<string, long> FileOffsets, long LastUpdateId) Load_OrEmpty(ISupervisionPaths paths, IOrchestrationLog? log)
    {
        Dictionary<string, long> emptyOffsets = [];

        if (!File.Exists(paths.BridgeStateFile))
        {
            // Normal on a first run and never normal afterwards, and the store cannot tell the two
            // apart from here — so it states the CONSEQUENCE and leaves the judgement to the reader.
            // Silence was the defect: this is the same one-way hole as a corrupt file, with nothing
            // to quarantine and, until now, nothing at any log level either.
            log?.Log_Warning(GLOBAL_ORCH_ID, Describe_EmptyCursor(paths, "does not exist"));
            return (emptyOffsets, 0L);
        }

        string text;

        try
        {
            text = File.ReadAllText(paths.BridgeStateFile);
        }
        catch (Exception readException)
        {
            // Broad by intent: locked, denied, unreadable sector — the cause changes nothing here.
            // The file may well be intact, so it is NOT quarantined; this run just starts blind.
            log?.Log_Warning(GLOBAL_ORCH_ID, Describe_EmptyCursor(paths, $"could not be read ({readException.Message})"));
            return (emptyOffsets, 0L);
        }

        // Not quarantined: an empty file is also the ordinary shape of "created but never filled".
        // It is logged because it is equally the signature of an interrupted write, and it was the
        // last input here that produced an empty cursor at no log level at all.
        if (string.IsNullOrWhiteSpace(text))
        {
            log?.Log_Warning(GLOBAL_ORCH_ID, Describe_EmptyCursor(paths, "is empty"));
            return (emptyOffsets, 0L);
        }

        try
        {
            var parsedState = Parse_State_OrNull(text);

            if (parsedState != null)
                return parsedState.Value;
        }
        catch (Exception parseException)
        {
            // Broad by intent: every way a half-written file fails to parse (truncated JSON, a value
            // of the wrong type) is the same recoverable situation, and it is reported below.
            Quarantine_And_Report(paths, log, "is not valid bridge state — half-written or damaged", parseException);
            return (emptyOffsets, 0L);
        }

        Quarantine_And_Report(paths, log, "parsed as JSON but its root is not an object", null);
        return (emptyOffsets, 0L);
    }

    /// <summary>
    /// Writes the cursor atomically. The folder is created by the writer, so the old explicit
    /// <c>Directory.CreateDirectory</c> would only repeat work already done one call deeper.
    /// </summary>
    public static void Save(ISupervisionPaths paths, IReadOnlyDictionary<string, long> fileOffsets, long lastUpdateId)
    {
        var offsetsObject = new JsonObject();

        foreach (var pair in fileOffsets)
            offsetsObject[pair.Key] = pair.Value;

        var root = new JsonObject
        {
            ["fileOffsets"] = offsetsObject,
            ["lastUpdateId"] = lastUpdateId,
        };

        // Rewritten ~30 times a minute: a plain truncate-then-write is one full disk away from
        // leaving a zero-length or half-written cursor behind. The rename cannot do that.
        Atomic_FileWriter.Write_AllText(paths.BridgeStateFile, root.ToJsonString(JsonWriting.INDENTED));
    }

    /// <summary>
    /// Returns the parsed cursor, or null when the text is well-formed JSON of the wrong shape.
    /// Throws for malformed JSON — the caller treats both outcomes as corruption.
    /// </summary>
    static (IReadOnlyDictionary<string, long> FileOffsets, long LastUpdateId)? Parse_State_OrNull(string text)
    {
        if (JsonNode.Parse(text) is not JsonObject root)
            return null;

        Dictionary<string, long> offsets = [];

        if (root["fileOffsets"] is JsonObject offsetsObject)
        {
            foreach (var pair in offsetsObject)
            {
                if (pair.Value != null)
                    offsets[pair.Key] = pair.Value.GetValue<long>();
            }
        }

        var lastUpdateIdNode = root["lastUpdateId"];
        var lastUpdateId = lastUpdateIdNode == null ? 0L : lastUpdateIdNode.GetValue<long>();

        // Named local so the tuple is built at the exact element types before it is lifted to nullable.
        (IReadOnlyDictionary<string, long> FileOffsets, long LastUpdateId) parsedState = (offsets, lastUpdateId);
        return parsedState;
    }

    static void Quarantine_And_Report(ISupervisionPaths paths, IOrchestrationLog? log, string reason, Exception? cause)
    {
        var quarantinePath = Move_Aside_OrNull(paths.BridgeStateFile);

        var evidence = quarantinePath == null
            ? "it could NOT be moved aside, so the next save overwrites it"
            : $"it was moved aside to '{quarantinePath}' for inspection";

        log?.Log_Error(
            GLOBAL_ORCH_ID,
            $"{Describe_EmptyCursor(paths, reason)}; {evidence}",
            cause);
    }

    /// <summary>
    /// ONE SENTENCE FOR ALL FOUR PATHS to an empty cursor (missing, unreadable, empty, corrupt), so
    /// none of them can drift into describing a different consequence than the others. It used to
    /// promise DUPLICATES — "may arrive a second time" — which is worse than saying nothing: a reader
    /// who believes it goes looking for repeated messages and concludes nothing was lost, when what
    /// actually happened is that channel entries written before this start were never mirrored.
    /// </summary>
    static string Describe_EmptyCursor(ISupervisionPaths paths, string reason)
    {
        return $"Bridge cursor file '{paths.BridgeStateFile}' {reason} — starting from an EMPTY cursor: every channel is baselined at its current length, so entries written before this start are NOT mirrored (a hole, not duplicates)";
    }

    /// <summary>Renames the damaged file to a timestamped sibling; returns null if that failed.</summary>
    static string? Move_Aside_OrNull(string filePath)
    {
        var folder = Path.GetDirectoryName(filePath);
        var stem = Path.GetFileNameWithoutExtension(filePath);
        var extension = Path.GetExtension(filePath);
        var quarantineName = $"{stem}.{CORRUPT_FILE_MARKER}-{DateTime.Now.ToString(QUARANTINE_STAMP_FORMAT)}{extension}";

        var quarantinePath = string.IsNullOrEmpty(folder)
            ? quarantineName
            : Path.Combine(folder, quarantineName);

        try
        {
            // Overwrite so a second corruption inside the same second still clears the live path;
            // keeping the older copy is worth less than getting the bridge back to a clean cursor.
            File.Move(filePath, quarantinePath, overwrite: true);
            return quarantinePath;
        }
        catch
        {
            // Broad by intent: quarantine is a diagnostic courtesy, not the recovery itself. Failing
            // to preserve the evidence must not fail the load — the caller reports the miss instead.
            return null;
        }
    }
}
