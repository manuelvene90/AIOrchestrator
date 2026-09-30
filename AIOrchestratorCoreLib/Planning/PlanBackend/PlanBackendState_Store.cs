using System.Globalization;
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Storage;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Planning.PlanBackend;

/// <summary>
/// Reads and writes <c>.plan-backend.json</c> beside the orchestration's PLAN.md.
///
/// <para>
/// ONE BAD FIELD COSTS ONE ENTRY, NEVER THE FILE. The first version wrapped the whole parse in a single
/// catch, so a single <c>"ownerRequestNumber": "1"</c> — an older writer, a hand-edit, a future version
/// — returned an EMPTY state: every request forgotten, the orchestration-closed stamp forgotten, and
/// the next tick re-acknowledged, re-reported and re-announced everything upstream. Three duplicate
/// calls out of one typo, from the file whose entire job is preventing them. Every value is now read
/// defensively and a broken entry is dropped alone.
/// </para>
/// <para>
/// WRITTEN THROUGH <see cref="Atomic_FileWriter"/>: a truncated state file is that same memory loss,
/// and this one is rewritten on every ingestion.
/// </para>
/// </summary>
public static class PlanBackendState_Store
{
    public static PlanBackendState Read(ISupervisionPaths paths, string orchId)
    {
        try
        {
            var file = paths.Get_PlanBackendStateFile(orchId);

            if (!File.Exists(file))
                return PlanBackendState.Empty();

            if (JsonNode.Parse(Safe_FileReader.Read_AllText_OrEmpty(file)) is not JsonObject root)
                return PlanBackendState.Empty();

            List<TrackedPlanRequest> requests = [];

            if (root["requests"] is JsonArray array)
            {
                foreach (var node in array)
                {
                    var tracked = Read_Request_OrNull(node as JsonObject);

                    if (tracked != null)
                        requests.Add(tracked);
                }
            }

            return new PlanBackendState(
                requests,
                Read_Utc_OrNull(root, "orchestrationClosedReportedUtc"),
                Read_Utc_OrNull(root, "appPlanWriteStampUtc"),
                Read_String_OrNull(root, "appPlanWriteHash"));
        }
        catch
        {
            return PlanBackendState.Empty();
        }
    }

    public static void Write(ISupervisionPaths paths, string orchId, PlanBackendState state)
    {
        var array = new JsonArray();

        foreach (var request in state.Requests)
        {
            array.Add(new JsonObject
            {
                ["requestId"] = request.RequestId,
                ["ledgerRowRef"] = request.LedgerRowRef,
                ["ownerRequestNumber"] = request.OwnerRequestNumber,
                ["acknowledgedUtc"] = Write_Utc_OrNull(request.AcknowledgedUtc),
                ["closedReportedUtc"] = Write_Utc_OrNull(request.ClosedReportedUtc),
            });
        }

        var root = new JsonObject
        {
            ["requests"] = array,
            ["orchestrationClosedReportedUtc"] = Write_Utc_OrNull(state.OrchestrationClosedReportedUtc),
            ["appPlanWriteStampUtc"] = Write_Utc_OrNull(state.AppPlanWriteStampUtc),
            ["appPlanWriteHash"] = state.AppPlanWriteHash,
        };

        Atomic_FileWriter.Write_AllText(paths.Get_PlanBackendStateFile(orchId), root.ToJsonString(JsonWriting.INDENTED));
    }

    /// <summary>
    /// Whether the plan file's CURRENT last-write time is the one the app itself left there — i.e. the
    /// newest write to PLAN.md is the app's own ingestion and no session has touched the file since.
    ///
    /// Read by <see cref="LedgerHealth_Tracker.Is_LedgerBehind"/>, which would otherwise take that write
    /// as the supervisor paying its ledger debt.
    ///
    /// <para>
    /// THE STAMP IS NECESSARY, NOT SUFFICIENT: a session write inside the same ~15.6 ms Windows timestamp
    /// tick carries the app's stamp too (see <see cref="PlanBackendState.AppPlanWriteHash"/>). When the
    /// stamps agree the file's bytes decide. The file is read only on that path — a stamp that differs
    /// answers without touching it.
    /// </para>
    /// </summary>
    public static bool Wrote_ThePlan_Itself(ISupervisionPaths paths, string orchId, DateTime planWriteUtc)
    {
        var state = Read(paths, orchId);

        if (state.AppPlanWriteStampUtc == null || state.AppPlanWriteStampUtc.Value != planWriteUtc)
            return false;

        if (state.AppPlanWriteHash == null)
            return true;

        return state.AppPlanWriteHash == Hash_File_OrNull(paths.Get_PlanFile(orchId));
    }

    /// <summary>
    /// SHA-256 of a file's bytes, lower-case hex; null when it cannot be read — which never matches a
    /// recorded hash, so an unreadable plan reads as "not the app's write" and the debt clears only
    /// through the stamp comparison the caller already made. Read with FileShare.ReadWrite so a session
    /// mid-write is not blocked by it.
    /// </summary>
    public static string? Hash_File_OrNull(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    static TrackedPlanRequest? Read_Request_OrNull(JsonObject? entry)
    {
        if (entry == null)
            return null;

        try
        {
            var requestId = Read_String_OrNull(entry, "requestId");
            var ledgerRowRef = Read_String_OrNull(entry, "ledgerRowRef");

            if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(ledgerRowRef))
                return null;

            return new TrackedPlanRequest(
                requestId,
                ledgerRowRef,
                Read_Int(entry, "ownerRequestNumber"),
                Read_Utc_OrNull(entry, "acknowledgedUtc"),
                Read_Utc_OrNull(entry, "closedReportedUtc"));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>A value of the wrong JSON type reads as absent rather than throwing — see the class doc.</summary>
    static string? Read_String_OrNull(JsonObject root, string key)
    {
        return root[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }

    static int Read_Int(JsonObject root, string key)
    {
        if (root[key] is not JsonValue value)
            return 0;

        if (value.TryGetValue<int>(out var number))
            return number;

        return value.TryGetValue<string>(out var text) && int.TryParse(text, out var parsed) ? parsed : 0;
    }

    static DateTime? Read_Utc_OrNull(JsonObject root, string key)
    {
        var text = Read_String_OrNull(root, key);

        return DateTime.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var value)
            ? value
            : null;
    }

    static string? Write_Utc_OrNull(DateTime? value)
    {
        return value?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
    }
}
