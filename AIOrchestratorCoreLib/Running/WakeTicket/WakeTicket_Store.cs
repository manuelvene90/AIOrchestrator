using System.Text.Json;
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Storage;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.WakeTicket;

/// <summary>
/// Reads and writes the wake ticket, and knows where it lives for each role — MIRRORING
/// <see cref="PrintSessionState_Store.Get_StateFile"/> exactly, because the two files describe the
/// same session and a third naming scheme here would be a third place to get it wrong.
///
/// <para>
/// THE FORMAT IS A CONTRACT ACROSS A LANGUAGE BOUNDARY: one line of compact JSON, camelCase keys, read
/// by the C# app AND by the shrunken bash watcher (2026-09-15 one-wake-model spec) with plain shell
/// tools. Do not reshape it without checking who else parses it.
/// </para>
/// </summary>
public static class WakeTicket_Store
{
    public const string TICKET_FILE_NAME = ".wake";

    /// <summary>
    /// Same three (four, counting <see cref="SessionRoles.Communicator"/>) shapes
    /// <see cref="PrintSessionState_Store.Get_StateFile"/> uses: a member's own folder, the general
    /// supervisor's home, and — for the singleton roles that share the orchestration folder with the
    /// session file — a role-prefixed name so a supervisor's and a communicator's tickets cannot
    /// collide.
    /// </summary>
    public static string Get_File(ISupervisionPaths paths, SessionRoles role, string orchId, string memberId)
    {
        return role switch
        {
            SessionRoles.Implementer or SessionRoles.Reviewer or SessionRoles.Solo => Path.Combine(paths.Get_ImplementerFolder(orchId, memberId), TICKET_FILE_NAME),
            SessionRoles.General => Path.Combine(paths.GeneralFolder, TICKET_FILE_NAME),
            SessionRoles.Supervisor => Path.Combine(paths.Get_OrchestrationFolder(orchId), $"{TICKET_FILE_NAME}-supervisor"),
            SessionRoles.Communicator => Path.Combine(paths.Get_OrchestrationFolder(orchId), $"{TICKET_FILE_NAME}-communicator"),
            _ => throw new Exception($"Unhandled SessionRoles: {role}"),
        };
    }

    /// <summary>
    /// Null for a missing file, a corrupt one, or one caught mid-write — a monitor that cannot read its
    /// ticket must not crash the app, and an app that cannot read its own last ticket falls back to
    /// ticket number 0 in the sweep rather than throwing.
    /// </summary>
    public static IWakeTicket? Read_OrNull(string ticketFile)
    {
        string text;

        try
        {
            text = Tolerant_FileReader.Read_AllText(ticketFile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        JsonObject? root;

        try
        {
            root = JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }

        if (root == null)
            return null;

        if (root["number"]?.GetValue<int>() is not int number)
            return null;

        if (root["reason"]?.GetValue<string>() is not string reason || reason.Length == 0)
            return null;

        if (root["stampedUtc"]?.GetValue<string>() is not string stampedText ||
            !DateTime.TryParse(stampedText, null, System.Globalization.DateTimeStyles.RoundtripKind, out var stampedUtc))
            return null;

        var statePackFile = root["statePackFile"]?.GetValue<string?>();

        try
        {
            return WakeTicket_Factory.Create(number, reason, statePackFile, stampedUtc.ToUniversalTime());
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the ticket whole via <see cref="Atomic_FileWriter"/> — temp file, then rename — so the
    /// watcher, which polls on its own clock, can never read a half-written ticket. Compact, single
    /// line, camelCase: see the type header.
    /// </summary>
    public static void Write(string ticketFile, IWakeTicket ticket)
    {
        var root = new JsonObject
        {
            ["number"] = ticket.Number,
            ["reason"] = ticket.Reason,
            ["statePackFile"] = ticket.StatePackFile,
            ["stampedUtc"] = ticket.StampedUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
        };

        Atomic_FileWriter.Write_AllText(ticketFile, root.ToJsonString());
    }
}
