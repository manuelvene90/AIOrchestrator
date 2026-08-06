using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AIOrchestrator.Views;
using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.Configuration.RepoEntry;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Logging;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using System.Collections.ObjectModel;
using AIOrchestratorCoreLib.Status;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestrator;

public partial class MainWindow : Window
{
    const int REFRESH_INTERVAL_SECONDS = 5;
    const int MAX_LOG_ROWS = 500;

    readonly ObservableCollection<LogRowView> _logRows = [];

    readonly ISupervisionPaths _paths;
    readonly IOrchestratorConfig _config;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;
    readonly DispatcherTimer _refreshTimer;

    public MainWindow(
        ISupervisionPaths paths,
        IOrchestratorConfig config,
        IOrchestrationSessionStore store,
        IOrchestrationLauncher launcher,
        IBridgeEngine engine,
        IOrchestrationLog log)
    {
        _paths = paths;
        _config = config;
        _store = store;
        _launcher = launcher;
        _engine = engine;

        InitializeComponent();

        ReposListBox.ItemsSource = config.Repos;
        ActivityLogListBox.ItemsSource = _logRows;
        RootPathText.Text = paths.Root;
        TelegramStatusText.Text = config.Is_TelegramConfigured()
            ? "Telegram: mirror + remote input active"
            : "Telegram: not configured (file-only mode) — run install.ps1 to set it up";

        log.EntryLogged += On_LogEntry;
        engine.OrchestrationActivity += _ => Dispatcher.BeginInvoke(Refresh_Orchestrations);
        engine.MutedChanged += muted => Dispatcher.BeginInvoke(() =>
        {
            if (MuteTelegramCheckBox.IsChecked != muted)
                MuteTelegramCheckBox.IsChecked = muted;
        });

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(REFRESH_INTERVAL_SECONDS) };
        _refreshTimer.Tick += (_, _) => Refresh_Orchestrations();
        _refreshTimer.Start();

        Refresh_Orchestrations();
    }

    void On_LogEntry(AIOrchestratorCoreLib.Logging.OrchestrationLogEntry.IOrchestrationLogEntry entry)
    {
        var prefix = entry.OrchId.Length == 0 ? "" : $"[{entry.OrchId}] ";
        Add_LogRow(entry.Level, $"{prefix}{entry.Message}");
    }

    /// <summary>Thread-safe: log events arrive from the bridge's background threads.</summary>
    void Add_LogRow(LogLevels level, string message)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var row = new LogRowView
            {
                TimeText = DateTime.Now.ToString("HH:mm:ss"),
                Message = message,
                MessageBrush = Find_Brush(LogBrush_KeyFor(level)),
            };

            _logRows.Insert(0, row);

            if (_logRows.Count > MAX_LOG_ROWS)
                _logRows.RemoveAt(_logRows.Count - 1);
        });
    }

    static string LogBrush_KeyFor(LogLevels level)
    {
        return level switch
        {
            LogLevels.Info => "TextPrimary",
            LogLevels.Warning => "StateAwaitingReview",
            LogLevels.Error => "StateBlocked",
            _ => throw new Exception($"Unhandled LogLevels: {level}"),
        };
    }

    void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        _logRows.Clear();
    }

    void Refresh_Orchestrations()
    {
        try
        {
            List<OrchestrationCardView> cards = [];

            var generalCard = Build_GeneralCard_OrNull();
            if (generalCard != null)
                cards.Add(generalCard);

            foreach (var session in _store.Load_All().OrderByDescending(s => s.CreatedUtc))
                cards.Add(Build_Card(session));

            OrchestrationsItemsControl.ItemsSource = cards;
        }
        catch (Exception ex)
        {
            Add_LogRow(LogLevels.Error, $"UI refresh failed: {ex.Message}");
        }
    }

    OrchestrationCardView? Build_GeneralCard_OrNull()
    {
        if (!File.Exists(_paths.GeneralChannelFile))
            return null;

        var entries = ChannelEntry_Parser.Parse_All(Read_FileText_Safe(_paths.GeneralChannelFile));
        var lastActivity = Get_LastWriteText(_paths.GeneralChannelFile);

        var stateText = entries.Count == 0 ? "no traffic yet" : $"last: {entries[^1].Author.ToString().ToLowerInvariant()}";

        var row = new MemberRowView
        {
            MemberLabel = "GENERAL",
            RoleBrush = Find_Brush("AccentGeneral"),
            StateText = stateText,
            StateBrush = Find_Brush("StateNew"),
            LastActivityText = lastActivity,
            FocusTitleFragment = "GENERAL",
        };

        return new OrchestrationCardView
        {
            OrchId = "general supervisor",
            RepoName = "always-on · pinned General topic",
            Members = [row],
        };
    }

    OrchestrationCardView Build_Card(IOrchestrationSession session)
    {
        List<MemberRowView> rows = [Build_SupervisorRow(session)];

        foreach (var member in session.Members)
            rows.Add(Build_MemberRow(session, member.MemberId, member.ClosedUtc != null));

        var openImplementers = session.Members.Count(m => m.ClosedUtc == null);
        var age = DateTime.UtcNow - session.CreatedUtc;

        return new OrchestrationCardView
        {
            OrchId = session.OrchId,
            RepoName = session.RepoName,
            SummaryText = $"· {openImplementers} implementer{(openImplementers == 1 ? "" : "s")} · running {Describe_Duration(age)}",
            IsClosed = session.ClosedUtc != null,
            ClosedLabel = session.ClosedUtc == null ? "" : "CLOSED",
            CardOpacity = session.ClosedUtc == null ? 1.0 : 0.5,
            Members = rows,
        };
    }

    MemberRowView Build_SupervisorRow(IOrchestrationSession session)
    {
        var ownerChannel = _paths.Get_OwnerChannelFile(session.OrchId);
        var lastActivity = File.Exists(ownerChannel) ? Get_LastWriteText(ownerChannel) : "";

        return new MemberRowView
        {
            MemberLabel = "SUPERVISOR",
            RoleBrush = Find_Brush("AccentSupervisor"),
            StateText = session.ClosedUtc == null ? "supervising" : "closed",
            StateBrush = session.ClosedUtc == null ? Find_Brush("StateWorking") : Find_Brush("StateClosed"),
            LastActivityText = lastActivity,
            FocusTitleFragment = $"SUP · {session.OrchId}",
            DetailText = Build_SupervisorCostText(session.OrchId),
        };
    }

    string Build_SupervisorCostText(string orchId)
    {
        var usageFile = Path.Combine(_paths.Get_OrchestrationFolder(orchId), ".usage.json");
        var cost = Read_SessionCost_OrNull(usageFile);

        return cost == null ? "" : $"session cost ${cost.Value:F2}";
    }

    MemberRowView Build_MemberRow(IOrchestrationSession session, string memberId, bool isClosed)
    {
        var channelFile = _paths.Get_ImplementerChannelFile(session.OrchId, memberId);

        if (isClosed)
        {
            return new MemberRowView
            {
                MemberLabel = memberId,
                RoleBrush = Find_Brush("AccentImplementer"),
                StateText = "closed",
                StateBrush = Find_Brush("StateClosed"),
                LastActivityText = "",
            };
        }

        var entries = ChannelEntry_Parser.Parse_All(Read_FileText_Safe(channelFile));
        var state = MemberState_Resolver.Resolve(entries);
        var usageFile = _paths.Get_ImplementerPidFile(session.OrchId, memberId).Replace(".pid", ".usage.json");

        return new MemberRowView
        {
            MemberLabel = memberId,
            RoleBrush = Find_Brush("AccentImplementer"),
            StateText = Describe_State(state),
            StateBrush = Find_Brush(Brush_KeyFor(state)),
            LastActivityText = File.Exists(channelFile) ? Get_LastWriteText(channelFile) : "",
            DetailText = Build_MemberDetailText(entries, usageFile),
            FocusTitleFragment = $"{memberId.ToUpperInvariant()} · {session.OrchId}",
        };
    }

    /// <summary>Second row per member: current task · worktree (from the brief's WORKTREE: marker) · time on task · session cost.</summary>
    static string Build_MemberDetailText(
        IReadOnlyList<AIOrchestratorCoreLib.Channels.ChannelEntry.IChannelEntry> entries,
        string usageFilePath)
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
            parts.Add($"${cost.Value:F2}");

        return string.Join("  ·  ", parts);
    }

    static string? Find_LastWorktreeMarker_OrNull(IReadOnlyList<AIOrchestratorCoreLib.Channels.ChannelEntry.IChannelEntry> entries)
    {
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            if (entries[i].Author != ChannelAuthors.Supervisor)
                continue;

            var match = System.Text.RegularExpressions.Regex.Match(
                entries[i].RawText, @"^WORKTREE:\s*(.+)$", System.Text.RegularExpressions.RegexOptions.Multiline);

            if (match.Success)
                return match.Groups[1].Value.Trim();
        }

        return null;
    }

    static string? Describe_TimeSince_OrNull(string entryDateText)
    {
        if (!DateTime.TryParse(entryDateText, out var entryTime))
            return null;

        return Describe_Duration(DateTime.Now - entryTime);
    }

    static string Describe_Duration(TimeSpan duration)
    {
        if (duration.TotalMinutes < 1)
            return "under a minute";
        if (duration.TotalHours < 1)
            return $"{(int)duration.TotalMinutes} min";
        if (duration.TotalDays < 1)
            return $"{(int)duration.TotalHours} h {duration.Minutes} min";

        return $"{(int)duration.TotalDays} d {duration.Hours} h";
    }

    static double? Read_SessionCost_OrNull(string usageFilePath)
    {
        try
        {
            if (!File.Exists(usageFilePath))
                return null;

            var root = System.Text.Json.Nodes.JsonNode.Parse(Read_FileText_Safe(usageFilePath));
            var costNode = root?["cost"]?["total_cost_usd"];

            if (costNode == null)
                return null;

            return costNode.GetValue<double>();
        }
        catch
        {
            return null;
        }
    }

    static string Describe_State(MemberStates state)
    {
        return state switch
        {
            MemberStates.NewNoTraffic => "new — no traffic",
            MemberStates.ImplementerWorking => "working",
            MemberStates.AwaitingSupervisorReview => "awaiting review",
            MemberStates.WritingWindowOpen => "window open",
            MemberStates.BlockedOnOwner => "BLOCKED ON OWNER",
            _ => throw new Exception($"Unhandled MemberStates: {state}"),
        };
    }

    static string Brush_KeyFor(MemberStates state)
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

    Brush Find_Brush(string resourceKey)
    {
        if (FindResource(resourceKey) is Brush brush)
            return brush;

        return Brushes.Gray;
    }

    static string Read_FileText_Safe(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return string.Empty;

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch
        {
            return string.Empty;
        }
    }

    static string Get_LastWriteText(string filePath)
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

    void StartSupervisorButton_Click(object sender, RoutedEventArgs e)
    {
        if (ReposListBox.SelectedItem is not IRepoEntry repo)
        {
            MessageBox.Show("Select a repository first.", "AI Orchestrator");
            return;
        }

        try
        {
            _launcher.Start_Orchestration(repo.Name, repo.Path);
            Refresh_Orchestrations();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Start Orchestration failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_paths, _config) { Owner = this };
        settingsWindow.ShowDialog();
    }

    void MuteTelegramCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _engine.Set_TelegramMuted(MuteTelegramCheckBox.IsChecked == true);
    }

    void ShowSessionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not MemberRowView row)
            return;

        if (row.FocusTitleFragment.Length == 0)
            return;

        var focused = AIOrchestratorCoreLib.WindowFocus.TerminalWindow_Focuser.Try_Focus_ByTitleFragment(row.FocusTitleFragment);

        if (!focused)
            Add_LogRow(LogLevels.Warning, $"No terminal window found titled '{row.FocusTitleFragment}' — is the session running?");
    }

    void AddImplementerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not OrchestrationCardView card)
            return;

        try
        {
            _launcher.Add_Implementer(card.OrchId);
            Refresh_Orchestrations();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Add Implementer failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not OrchestrationCardView card)
            return;

        var folder = card.OrchId == "general supervisor"
            ? _paths.GeneralFolder
            : _paths.Get_OrchestrationFolder(card.OrchId);

        Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
    }
}
