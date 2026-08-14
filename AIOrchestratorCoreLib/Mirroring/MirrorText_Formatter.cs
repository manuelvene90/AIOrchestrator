using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Channels.DiscoveredChannel;

namespace AIOrchestratorCoreLib.Mirroring;

/// <summary>
/// Formats channel entries for the Telegram mirror — MINIMAL VERBOSITY by owner mandate.
/// Only owner ⇄ supervisor channels reach Telegram at all (implementer spokes stay in the app);
/// prefixes are bare ("Sup:", "Gen-Sup:", "Com:", "App:") — the owner knows who they are; no entry
/// numbers, no direction arrows, no subject-plus-body duplication. Each voice has its own color:
/// 🔴 orchestration supervisor · 🟡 general supervisor · 🟢 communicator · 🔵 implementer ·
/// 🟢 reviewer · ⚙ app. The reviewer's green matches its terminal tab and its row in the app
/// (inherited from the retired communicator).
/// </summary>
public static class MirrorText_Formatter
{
    /// <summary>Supervisors title periodic status entries with this; DND catch-up keeps only the newest.</summary>
    public const string STATUS_SUBJECT_PREFIX = "STATUS";

    public static bool Should_Mirror(IDiscoveredChannel channel, IChannelEntry entry)
    {
        // Member spokes: ONLY the presence one-liner ("imp-1 online", "rev-1 online") reaches
        // Telegram — briefs and reports made the topics unreadable; the owner reads those in the app.
        if (!channel.IsOwnerChannel)
            return ChannelAuthor_Kinds.Is_Member(entry.Author) && Is_PresenceEntry(channel, entry);

        // App entries that COACH THE AGENT are written to the owner channel because that is the
        // only thing the supervisor reads — they were never meant for the owner, and texting them
        // is how "⚙ App: unread reports waiting on you — rev-2" ended up on their phone, reading as
        // if it were addressed to them. The owner-facing app notices are sent separately and
        // directly, so nothing is lost by keeping these internal.
        if (entry.Author == ChannelAuthors.App && Is_AgentCoaching(entry.Subject))
            return false;

        // Owner entries came FROM Telegram (or the owner's own terminal) — never echoed back.
        return entry.Author != ChannelAuthors.Owner;
    }

    public static string Format(IDiscoveredChannel channel, IChannelEntry entry)
    {
        // The spoke's own kind decides the colour — a reviewer must not read as an implementer,
        // since what it is licensed to do is completely different.
        if (!channel.IsOwnerChannel)
            return $"{Glyph_ForSpoke(channel.SpokeName)} {channel.SpokeName}: online";

        return entry.Author switch
        {
            // App entries: the subject IS the message ("orchestration 'crm-2' closed"); the body
            // is agent-facing detail the owner explicitly does not want texted. The ONE exception
            // is the periodic STATUS, whose body is the report — it rides the channel precisely so
            // that Do-Not-Disturb queues it (and collapses it to the newest) instead of dropping it.
            ChannelAuthors.App => Is_StatusEntry(entry) ? Pick_Content(entry) : $"⚙ App: {entry.Subject}",

            // Supervisor entries: the body is the message; the subject is channel metadata.
            // The GENERAL supervisor speaks with its own color so the owner never confuses
            // the concierge with an orchestration's supervisor.
            ChannelAuthors.Supervisor => channel.OrchId == ChannelDiscovery.GENERAL_ORCH_ID
                ? $"🟡 Gen-Sup: {Pick_Content(entry)}"
                : $"🔴 Sup: {Pick_Content(entry)}",

            // Communicator entries: live narration of what the supervisor is doing — green voice.
            ChannelAuthors.Communicator => $"🟢 Com: {Pick_Content(entry)}",

            // A BASIC orchestration's single session speaks here by design — it IS the conversation,
            // there is no supervisor between it and the owner.
            ChannelAuthors.Solo => $"🟠 {Pick_Content(entry)}",

            // Members of an ORCHESTRATED session never write on the owner channel; if one does,
            // flag it rather than hide it.
            ChannelAuthors.Implementer => $"⚠ Imp?: {Pick_Content(entry)}",
            ChannelAuthors.Reviewer => $"⚠ Rev?: {Pick_Content(entry)}",
            ChannelAuthors.Owner => throw new Exception($"Owner entries must be filtered by Should_Mirror (channel '{channel.FilePath}')"),
            ChannelAuthors.Unknown => $"?: {Pick_Content(entry)}",
            _ => throw new Exception($"Unhandled ChannelAuthors: {entry.Author}"),
        };
    }

    /// <summary>Spoke colour by member kind, read off the member id (imp-n / rev-n).</summary>
    static string Glyph_ForSpoke(string spokeName)
    {
        return Sessions.MemberKind_Ids.Resolve_Kind(spokeName) switch
        {
            Sessions.MemberKinds.Reviewer => "🟢",
            Sessions.MemberKinds.Implementer => "🔵",
            _ => throw new Exception($"Unhandled MemberKinds for spoke '{spokeName}'"),
        };
    }

    /// <summary>
    /// App entries whose audience is the AGENT, not the owner. Listed explicitly rather than
    /// suppressing app entries wholesale: some genuinely are owner news ("implementer 'imp-2'
    /// added — <reason>", "orchestration closed", a rejected request), and losing those would be a
    /// worse fault than the noise this removes.
    /// </summary>
    static readonly IReadOnlyList<string> AGENT_COACHING_SUBJECTS =
    [
        "unread reports waiting on you",

        // Written at BridgeEngineModel's owner-reply nudge and NEVER listed here until 2026-08-14, so
        // it was mirrored: the owner spent the day receiving "⚙ App: the owner is still waiting for
        // your reply" — themselves told that they are waiting for their own reply. The body that
        // explains it ("YOUR turn ended without answering the owner's message above") is agent-facing
        // and never leaves the channel, which is what makes the subject read as nonsense rather than
        // as a visible mistake.
        "the owner is still waiting for your reply",

        "that message was too long for a phone",
        "HOLD — the owner has not answered",
        "AWAY MODE ON",
        "AWAY MODE OFF",
        "the owner switched this topic to Do-Not-Disturb",
        "Do-Not-Disturb is off",
        "GO AHEAD — resume",
        "unread traffic — you have not answered",
        Status.Retirement_Advisor.FLAG_SUBJECT,
        Status.GuardNotInForce_Marker.ENTRY_SUBJECT,
        "you stopped mid-task",
        "INVISIBLE — malformed header",
        "entries are INVISIBLE",
        "entry is INVISIBLE",
        "session was orphaned",
        "TASK LEDGER",
        "PLAN.md",
    ];

    static bool Is_AgentCoaching(string subject)
    {
        foreach (var coaching in AGENT_COACHING_SUBJECTS)
        {
            if (subject.Contains(coaching, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
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
