using System.Globalization;
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Storage;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Reviewing;

/// <summary>
/// The OPEN re-review contracts of one orchestration, in <c>&lt;orch folder&gt;/reroute.json</c> —
/// beside <c>session.json</c>, outside every git tree, following <c>WakeTicket_Store</c> exactly.
///
/// <para>
/// ABSENT IS THE COMMON CASE AND READS AS NONE. Most orchestrations never declare a contract, and
/// they must pay nothing for this feature — so a missing file, a corrupt one and one caught
/// mid-write are all "no contracts", never a throw. This file is read on every engine tick.
/// </para>
/// <para>
/// THE FILE IS A WORKING SET, NOT AN AUDIT TRAIL. Closing a contract REMOVES it: the audit trail is
/// <c>orchestrator.log.jsonl</c>, which every close writes one line to (decision 21 — name what
/// happened rather than going silent). A contract that stays in the file for ever is a hold that
/// stays armed for ever, and a hold is what keeps a supervisor from being woken.
/// </para>
/// </summary>
public static class RerouteContract_Store
{
    public const string FILE_NAME = "reroute.json";

    /// <summary>The round-trip stamp format — ISO-8601 UTC, the same shape <c>WakeTicket_Store</c> writes.</summary>
    const string STAMP_FORMAT = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public static string Get_File(ISupervisionPaths paths, string orchId)
    {
        return Path.Combine(paths.Get_OrchestrationFolder(orchId), FILE_NAME);
    }

    /// <summary>
    /// Every open contract, in the order written. A row that cannot be read WHOLE is dropped and the
    /// rest survive: a contract missing a reviewer id would address a relay to nobody, and one bad
    /// row must not take a good one with it.
    /// </summary>
    public static IReadOnlyList<IRerouteContract> Read_Open(ISupervisionPaths paths, string orchId)
    {
        string text;

        try
        {
            text = Tolerant_FileReader.Read_AllText(Get_File(paths, orchId));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            return [];
        }

        JsonObject? root;

        try
        {
            root = JsonNode.Parse(text) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }

        if (root?["contracts"] is not JsonArray rows)
            return [];

        List<IRerouteContract> contracts = [];

        foreach (var row in rows)
        {
            var contract = Read_Row_OrNull(row as JsonObject);

            if (contract != null)
                contracts.Add(contract);
        }

        return contracts;
    }

    /// <summary>
    /// Replaces the whole set. Written whole via <see cref="Atomic_FileWriter"/> — temp file then
    /// rename — because the engine tick and a restarting app can both be at this file, and a
    /// half-written working set is a hold in an unknown state.
    /// </summary>
    public static void Write_Open(ISupervisionPaths paths, string orchId, IReadOnlyList<IRerouteContract> contracts)
    {
        JsonArray rows = [];

        foreach (var contract in contracts)
            rows.Add(Write_Row(contract));

        Atomic_FileWriter.Write_AllText(Get_File(paths, orchId), new JsonObject { ["contracts"] = rows }.ToJsonString());
    }

    static JsonObject Write_Row(IRerouteContract contract)
    {
        return new JsonObject
        {
            ["id"] = contract.Id,
            ["orchId"] = contract.OrchId,
            ["implementerId"] = contract.ImplementerId,
            ["reviewerId"] = contract.ReviewerId,
            ["baseCommit"] = contract.BaseCommit,
            ["brief"] = contract.Brief,
            ["declaredUtc"] = Stamp(contract.DeclaredUtc),
            ["state"] = contract.State.ToString(),
            ["reportIdentity"] = contract.ReportIdentity,
            ["headCommit"] = contract.HeadCommit,
            ["routedUtc"] = contract.RoutedUtc == null ? null : Stamp(contract.RoutedUtc.Value),
        };
    }

    static IRerouteContract? Read_Row_OrNull(JsonObject? row)
    {
        if (row == null)
            return null;

        try
        {
            if (Read_String_OrNull(row, "declaredUtc") is not string declaredText
                || Parse_Stamp_OrNull(declaredText) is not DateTime declaredUtc)
                return null;

            var declared = RerouteContract_Factory.Create_Declared(
                id: Read_String_OrNull(row, "id") ?? string.Empty,
                orchId: Read_String_OrNull(row, "orchId") ?? string.Empty,
                implementerId: Read_String_OrNull(row, "implementerId") ?? string.Empty,
                reviewerId: Read_String_OrNull(row, "reviewerId") ?? string.Empty,
                baseCommit: Read_String_OrNull(row, "baseCommit") ?? string.Empty,
                brief: Read_String_OrNull(row, "brief") ?? string.Empty,
                declaredUtc: declaredUtc);

            if (Read_String_OrNull(row, "state") != RerouteStates.Routed.ToString())
                return declared;

            // A ROUTED ROW NEEDS ALL THREE OF ITS ROUTED FIELDS. One of them missing is a row written
            // by something that is not this file's writer, and half a routed contract holds a
            // supervisor out of the loop with no way to expire.
            if (Read_String_OrNull(row, "reportIdentity") is not string reportIdentity
                || Read_String_OrNull(row, "headCommit") is not string headCommit
                || Read_String_OrNull(row, "routedUtc") is not string routedText
                || Parse_Stamp_OrNull(routedText) is not DateTime routedUtc)
                return null;

            return RerouteContract_Factory.CreateFrom_Routed(declared, reportIdentity, headCommit, routedUtc);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or FormatException)
        {
            // The factory refused the row — a missing identifier. Drop this one, keep the others.
            return null;
        }
    }

    static string? Read_String_OrNull(JsonObject row, string key)
    {
        if (row[key] is not JsonValue value || !value.TryGetValue<string>(out var text))
            return null;

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    static string Stamp(DateTime moment)
    {
        return moment.ToUniversalTime().ToString(STAMP_FORMAT, CultureInfo.InvariantCulture);
    }

    static DateTime? Parse_Stamp_OrNull(string text)
    {
        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var moment))
            return null;

        return moment.ToUniversalTime();
    }
}
