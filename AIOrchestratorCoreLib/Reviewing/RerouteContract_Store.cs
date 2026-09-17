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
/// <para>
/// AND BESIDE THE WORKING SET, THE ONE THING REMOVAL CANNOT THROW AWAY: the ids of the declarations
/// the sweep is DONE WITH (<see cref="Read_Handled"/>). A declaration lives in the channel for ever
/// and the sweep reads the channel every tick, so a contract that is merely removed is re-opened from
/// the same <c>REROUTE:</c> line on the very next pass — and re-opened it matches the same fix report,
/// writes the reviewer a second relay, and holds the supervisor again. That is not a slow leak but a
/// loop, at the tick interval, and the reviewer re-reviews the same delta for ever. The handled list
/// is what makes "this round is over" a fact that outlives the row it closed, and it covers every way
/// a round can end: routed and answered, expired, cancelled, superseded, or refused at validation.
/// </para>
/// </summary>
public static class RerouteContract_Store
{
    public const string FILE_NAME = "reroute.json";

    /// <summary>
    /// How many finished declarations are remembered. Each is sixteen hex characters and one fix
    /// round, so this is generous against any orchestration anybody has run; the cap exists because
    /// this file is read on the tick and an unbounded list would grow for the life of the repo.
    ///
    /// <para>
    /// WHAT FALLING OFF THE END COSTS, said plainly: a declaration older than the last
    /// <see cref="HANDLED_MEMORY"/> rounds could be re-opened — but only while it is still the LAST
    /// <c>REROUTE:</c> directive in its implementer's live channel, which after two hundred further
    /// rounds it is not, and only while the fix report it matched is still in that live file, which
    /// <c>Channel_Compactor</c> has long since archived. The matcher refuses on both counts.
    /// </para>
    /// </summary>
    public const int HANDLED_MEMORY = 200;

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
        return Read_Set(paths, orchId).Open;
    }

    /// <summary>
    /// The ids of the declarations this orchestration is DONE WITH, oldest first. A caller that is
    /// about to open a contract asks this as well as <see cref="Read_Open"/>: a declaration named
    /// here has already had its round and must never open a second one.
    /// </summary>
    public static IReadOnlyList<string> Read_Handled(ISupervisionPaths paths, string orchId)
    {
        return Read_Set(paths, orchId).Handled;
    }

    /// <summary>
    /// BOTH HALVES OUT OF ONE READ, and that is the whole reason this method exists rather than two
    /// public readers that each open the file. The sweep needs both on every tick it runs, and two
    /// reads of the same file a millisecond apart can also disagree — the second one may see a write
    /// the first did not, which would let a contract be opened from a declaration the other half had
    /// just marked handled.
    /// </summary>
    public static (IReadOnlyList<IRerouteContract> Open, IReadOnlyList<string> Handled) Read_Set(ISupervisionPaths paths, string orchId)
    {
        string text;

        try
        {
            text = Tolerant_FileReader.Read_AllText(Get_File(paths, orchId));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            return ([], []);
        }

        JsonObject? root;

        try
        {
            root = JsonNode.Parse(text) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return ([], []);
        }

        List<IRerouteContract> contracts = [];

        if (root?["contracts"] is JsonArray rows)
        {
            foreach (var row in rows)
            {
                var contract = Read_Row_OrNull(row as JsonObject);

                if (contract != null)
                    contracts.Add(contract);
            }
        }

        List<string> handled = [];

        if (root?["handled"] is JsonArray handledRows)
        {
            foreach (var row in handledRows)
            {
                if (row is JsonValue value && value.TryGetValue<string>(out var id) && !string.IsNullOrWhiteSpace(id))
                    handled.Add(id);
            }
        }

        return (contracts, handled);
    }

    /// <summary>
    /// Replaces the whole set. Written whole via <see cref="Atomic_FileWriter"/> — temp file then
    /// rename — because the engine tick and a restarting app can both be at this file, and a
    /// half-written working set is a hold in an unknown state.
    /// </summary>
    public static void Write_Open(
        ISupervisionPaths paths,
        string orchId,
        IReadOnlyList<IRerouteContract> contracts,
        int? handledMemory = null)
    {
        // THE HANDLED LIST IS READ BACK AND RE-WRITTEN rather than dropped. This overload states only
        // the open set, and a writer that silently emptied the other half would un-finish every round
        // this orchestration has ever closed — the whole file is written whole, so leaving a key out
        // deletes it.
        Write_Set(paths, orchId, contracts, Read_Handled(paths, orchId), handledMemory);
    }

    /// <summary>
    /// Replaces BOTH halves. The handled list is kept to the most recent <paramref name="handledMemory"/>
    /// ids, in the order given — oldest first, so the trim drops the oldest rounds, which are the ones
    /// the channel no longer carries either.
    ///
    /// <para>
    /// <paramref name="handledMemory"/> IS THE OWNER'S <c>reviewing.handledMemory</c>, passed in by the
    /// engine, which is the caller holding a config provider (2026-09-17). Null falls to
    /// <see cref="HANDLED_MEMORY"/> — the SHIPPED DEFAULT, the same field the catalogue row reads its
    /// own default from, so "nothing configured" is stated once and not twice. It is not a convenience
    /// for production: the engine always passes a value, and the callers that leave it null are tests
    /// and the Write_Open path, neither of which has a config in reach.
    /// </para>
    /// </summary>
    public static void Write_Set(
        ISupervisionPaths paths,
        string orchId,
        IReadOnlyList<IRerouteContract> contracts,
        IReadOnlyList<string> handled,
        int? handledMemory = null)
    {
        JsonArray rows = [];

        foreach (var contract in contracts)
            rows.Add(Write_Row(contract));

        JsonArray handledRows = [];

        var keep = handledMemory ?? HANDLED_MEMORY;

        foreach (var id in handled.Skip(Math.Max(0, handled.Count - keep)))
            handledRows.Add(JsonValue.Create(id));

        Atomic_FileWriter.Write_AllText(
            Get_File(paths, orchId),
            new JsonObject { ["contracts"] = rows, ["handled"] = handledRows }.ToJsonString());
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
            ["relayIdentity"] = contract.RelayIdentity,
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

            // THE RELAY IDENTITY IS NOT IN THAT LIST, deliberately. The three above are what the relay
            // was MADE of, and a row missing one of them is half a routed contract; this one is what
            // the relay BECAME, is written a moment later, and its absence costs only the ordinary
            // exit — the cap still ends the hold.
            return RerouteContract_Factory.CreateFrom_Routed(
                declared, reportIdentity, headCommit, routedUtc, Read_String_OrNull(row, "relayIdentity"));
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
