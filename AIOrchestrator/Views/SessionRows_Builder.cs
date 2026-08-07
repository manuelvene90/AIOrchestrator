using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Status;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Usage;

namespace AIOrchestrator.Views;

/// <summary>
/// Turns an orchestration's on-disk state (channels, usage probes, session.json) into the row
/// view models. ONE source of truth for both the main window's cards and the detail window —
/// they must never drift apart. The brush resolver is injected because resource lookup belongs
/// to the window, not to this shaping logic.
/// </summary>
public static class SessionRows_Builder
{
    const string COMMUNICATOR_USAGE_FILE = ".communicator.usage.json";
    const string SUPERVISOR_USAGE_FILE = ".usage.json";

    public static IReadOnlyList<MemberRowView> Build_AllRows(
        ISupervisionPaths paths,
        Func<string, Brush> findBrush,
        IOrchestrationSession session)
    {
        List<MemberRowView> rows =
        [
            Build_SupervisorRow(paths, findBrush, session),
            Build_CommunicatorRow(paths, findBrush, session),
        ];

        foreach (var member in session.Members)
            rows.Add(Build_MemberRow(paths, findBrush, session, member));

        return rows;
    }

    public static MemberRowView Build_SupervisorRow(
        ISupervisionPaths paths,
        Func<string, Brush> findBrush,
        IOrchestrationSession session)
    {
        var ownerChannel = paths.Get_OwnerChannelFile(session.OrchId);
        var isOpen = session.ClosedUtc == null;
        var supervisorUsageFile = Path.Combine(paths.Get_OrchestrationFolder(session.OrchId), SUPERVISOR_USAGE_FILE);
        var cost = Read_SessionCost_OrNull(supervisorUsageFile);
        var isWorkingNow = isOpen && SessionActivity_Probe.Is_MidTurn(supervisorUsageFile);

        return new MemberRowView
        {
            MemberLabel = "SUPERVISOR",
            RoleBrush = findBrush("AccentSupervisor"),
            StateText = isOpen ? (isWorkingNow ? "working now" : "idle — waiting") : "closed",
            StateBrush = isOpen ? findBrush("StateWorking") : findBrush("StateClosed"),
            LastActivityText = File.Exists(ownerChannel) ? Get_LastWriteText(ownerChannel) : "",
            FocusTitleFragment = $"SUP · {session.OrchId}",

            // API-EQUIVALENT usage, not a charge — subscription plans are not billed per token.
            DetailText = cost == null ? "" : $"usage ≈${cost.Value:F2} equiv (not billed)",
            ShowButtonVisibility = isOpen ? Visibility.Visible : Visibility.Collapsed,
        };
    }

    /// <summary>The green press-secretary session: narrates the supervisor's activity, never works.</summary>
    public static MemberRowView Build_CommunicatorRow(
        ISupervisionPaths paths,
        Func<string, Brush> findBrush,
        IOrchestrationSession session)
    {
        var isOpen = session.ClosedUtc == null;
        var cost = Read_SessionCost_OrNull(Path.Combine(paths.Get_OrchestrationFolder(session.OrchId), COMMUNICATOR_USAGE_FILE));

        return new MemberRowView
        {
            MemberLabel = "COM",
            RoleBrush = findBrush("AccentCommunicator"),
            StateText = isOpen ? "on watch" : "closed",
            StateBrush = isOpen ? findBrush("AccentCommunicator") : findBrush("StateClosed"),
            LastActivityText = "",
            DetailText = cost == null ? "" : $"≈${cost.Value:F2} equiv",
            FocusTitleFragment = $"COM · {session.OrchId}",
            ShowButtonVisibility = isOpen ? Visibility.Visible : Visibility.Collapsed,
        };
    }

    public static MemberRowView Build_MemberRow(
        ISupervisionPaths paths,
        Func<string, Brush> findBrush,
        IOrchestrationSession session,
        AIOrchestratorCoreLib.Sessions.OrchestrationMember.IOrchestrationMember member)
    {
        var memberId = member.MemberId;
        var channelFile = paths.Get_ImplementerChannelFile(session.OrchId, memberId);

        var entries = ChannelEntry_Parser.Parse_All(SafeFile_Reader.Read_Text_Safe(channelFile));
        var state = MemberState_Resolver.Resolve(entries);
        var usageFile = paths.Get_ImplementerPidFile(session.OrchId, memberId).Replace(".pid", ".usage.json");

        var isClosed = member.ClosedUtc != null || session.ClosedUtc != null;

        // LIVE activity outranks the declared marker: "window open" only says an agent announced a
        // writing window and never closed it, which is stale the moment it starts working again.
        var isWorkingNow = !isClosed && SessionActivity_Probe.Is_MidTurn(usageFile);

        return new MemberRowView
        {
            MemberLabel = memberId,
            RoleBrush = findBrush("AccentImplementer"),
            StateText = isClosed ? "closed" : Describe_MemberState(state, isWorkingNow),
            StateBrush = isClosed ? findBrush("StateClosed") : findBrush(isWorkingNow ? "StateWorking" : Brush_KeyFor(state)),
            LastActivityText = File.Exists(channelFile) ? Get_LastWriteText(channelFile) : "",
            DetailText = Build_MemberDetailText(entries, usageFile),
            FocusTitleFragment = $"{memberId.ToUpperInvariant()} · {session.OrchId}",
            ShowButtonVisibility = isClosed ? Visibility.Collapsed : Visibility.Visible,

            // A closed member inside a still-open card dims on its own; closed cards dim as a whole.
            RowOpacity = member.ClosedUtc != null && session.ClosedUtc == null ? 0.55 : 1.0,
        };
    }

    /// <summary>Second row per member: current task · time on task · worktree · session cost.</summary>
    public static string Build_MemberDetailText(IReadOnlyList<IChannelEntry> entries, string usageFilePath)
    {
        List<string> parts = [];

        var lastBrief = entries.LastOrDefault(e => e.Author == ChannelAuthors.Supervisor);

        if (lastBrief != null)
        {
            parts.Add($"task: {lastBrief.Subject}");

            var onTaskFor = Describe_TimeSince_OrNull(lastBrief.DateText);
            if (onTaskFor != null)
                parts.Add($"on task {onTaskFor}");

            var worktree = Find_LastWorktreeMarker_OrNull(entries);
            if (worktree != null)
                parts.Add($"wt: {worktree}");
        }

        var cost = Read_SessionCost_OrNull(usageFilePath);
        if (cost != null)
            parts.Add($"≈${cost.Value:F2} equiv");

        return string.Join("  ·  ", parts);
    }

    public static string? Find_LastWorktreeMarker_OrNull(IReadOnlyList<IChannelEntry> entries)
    {
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            if (entries[i].Author != ChannelAuthors.Supervisor)
                continue;

            var match = Regex.Match(entries[i].RawText, @"^WORKTREE:\s*(.+)$", RegexOptions.Multiline);

            if (match.Success)
                return match.Groups[1].Value.Trim();
        }

        return null;
    }

    /// <summary>Orchestration-wide LIFETIME usage — shared with the bridge's /tokens command.</summary>
    public static (double Cost, long Tokens) Build_UsageTotals(ISupervisionPaths paths, IOrchestrationSession session)
    {
        return UsageTotals_Reader.Build_OrchestrationTotals(paths, session);
    }

    public static string Format_Tokens(long tokens)
    {
        return UsageTotals_Reader.Format_Tokens(tokens);
    }

    public static string? Describe_TimeSince_OrNull(string entryDateText)
    {
        if (!DateTime.TryParse(entryDateText, out var entryTime))
            return null;

        return Describe_Duration(DateTime.Now - entryTime);
    }

    public static string Describe_Duration(TimeSpan duration)
    {
        if (duration.TotalMinutes < 1)
            return "under a minute";
        if (duration.TotalHours < 1)
            return $"{(int)duration.TotalMinutes} min";
        if (duration.TotalDays < 1)
            return $"{(int)duration.TotalHours} h {duration.Minutes} min";

        return $"{(int)duration.TotalDays} d {duration.Hours} h";
    }

    public static string Get_LastWriteText(string filePath)
    {
        var lastWrite = File.GetLastWriteTime(filePath);
        var elapsed = DateTime.Now - lastWrite;

        if (elapsed.TotalMinutes < 1)
            return "just now";
        if (elapsed.TotalHours < 1)
            return $"{(int)elapsed.TotalMinutes} min ago";
        if (elapsed.TotalDays < 1)
            return $"{(int)elapsed.TotalHours} h ago";

        return lastWrite.ToString("dd/MM HH:mm");
    }

    /// <summary>
    /// What the owner actually wants to know. A session whose transcript is growing is WORKING,
    /// whatever the channel markers claim; the declared state is shown alongside so the protocol
    /// information is not lost (e.g. "working now (writing window open)").
    /// </summary>
    public static string Describe_MemberState(MemberStates state, bool isWorkingNow)
    {
        if (!isWorkingNow)
            return Describe_State(state);

        return state switch
        {
            MemberStates.WritingWindowOpen => "working now (writing window open)",
            MemberStates.BlockedOnOwner => "working now (was blocked on you)",
            MemberStates.AwaitingSupervisorReview => "working now (report filed)",
            _ => "working now",
        };
    }

    public static string Describe_State(MemberStates state)
    {
        return state switch
        {
            MemberStates.NewNoTraffic => "new — no traffic",
            MemberStates.ImplementerWorking => "briefed — not started yet",
            MemberStates.AwaitingSupervisorReview => "awaiting review",
            MemberStates.WritingWindowOpen => "idle — writing window left open",
            MemberStates.BlockedOnOwner => "BLOCKED ON OWNER",
            _ => throw new Exception($"Unhandled MemberStates: {state}"),
        };
    }

    public static string Brush_KeyFor(MemberStates state)
    {
        return state switch
        {
            MemberStates.NewNoTraffic => "StateNew",
            MemberStates.ImplementerWorking => "StateWorking",
            MemberStates.AwaitingSupervisorReview => "StateAwaitingReview",
            MemberStates.WritingWindowOpen => "StateWindowOpen",
            MemberStates.BlockedOnOwner => "StateBlocked",
            _ => throw new Exception($"Unhandled MemberStates: {state}"),
        };
    }

    public static double? Read_SessionCost_OrNull(string usageFilePath)
    {
        return UsageTotals_Reader.Read_Cost_OrNull(usageFilePath);
    }
}
