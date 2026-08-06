using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Channels.DiscoveredChannel;

namespace AIOrchestratorCoreLib.Mirroring;

/// <summary>
/// Formats channel entries for the Telegram mirror — MINIMAL VERBOSITY by owner mandate.
/// Only owner ⇄ supervisor channels reach Telegram at all (implementer spokes stay in the app);
/// prefixes are bare ("Sup:", "App:") — the owner knows who they are; no entry numbers, no
/// direction arrows, no subject-plus-body duplication.
/// </summary>
public static class MirrorText_Formatter
{
    /// <summary>Supervisors title periodic status entries with this; DND catch-up keeps only the newest.</summary>
    public const string STATUS_SUBJECT_PREFIX = "STATUS";

    public static bool Should_Mirror(IDiscoveredChannel channel, IChannelEntry entry)
    {
        // Implementer spokes: ONLY the presence one-liner ("imp-1 online") reaches Telegram —
        // briefs and reports made the topics unreadable; the owner reads those in the app.
        if (!channel.IsOwnerChannel)
            return entry.Author == ChannelAuthors.Implementer && Is_PresenceEntry(channel, entry);

        // Owner entries came FROM Telegram (or the owner's own terminal) — never echoed back.
        return entry.Author != ChannelAuthors.Owner;
    }

    public static string Format(IDiscoveredChannel channel, IChannelEntry entry)
    {
        if (!channel.IsOwnerChannel)
            return $"🔵 {channel.SpokeName}: online";

        return entry.Author switch
        {
            // App entries: the subject IS the message ("orchestration 'crm-2' closed"); the body
            // is agent-facing detail the owner explicitly does not want texted.
            ChannelAuthors.App => $"⚙ App: {entry.Subject}",

            // Supervisor entries: the body is the message; the subject is channel metadata.
            ChannelAuthors.Supervisor => $"🔴 Sup: {Pick_Content(entry)}",

            ChannelAuthors.Implementer => $"⚠ Imp?: {Pick_Content(entry)}",
            ChannelAuthors.Owner => throw new Exception($"Owner entries must be filtered by Should_Mirror (channel '{channel.FilePath}')"),
            ChannelAuthors.Unknown => $"?: {Pick_Content(entry)}",
            _ => throw new Exception($"Unhandled ChannelAuthors: {entry.Author}"),
        };
    }

    public static bool Is_StatusEntry(IChannelEntry entry)
    {
        return entry.Subject.StartsWith(STATUS_SUBJECT_PREFIX, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The implementer boot entry ("imp-1 online[, …]") — the one spoke entry the owner sees.</summary>
    static bool Is_PresenceEntry(IDiscoveredChannel channel, IChannelEntry entry)
    {
        return entry.Subject.Trim().StartsWith($"{channel.SpokeName} online", StringComparison.OrdinalIgnoreCase);
    }

    static string Pick_Content(IChannelEntry entry)
    {
        return entry.Body.Length > 0 ? entry.Body : entry.Subject;
    }
}
