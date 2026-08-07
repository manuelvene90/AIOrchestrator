using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AIOrchestrator.Views;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestrator;

/// <summary>
/// Everything known about one orchestration on a single screen: the PLAN.md task ledger, the
/// merged activity feed (owner channel + every implementer spoke), and every session's live
/// state. Refreshes itself while open — the owner watches work happen here.
/// </summary>
public partial class OrchestrationDetailWindow : Window
{
    const int REFRESH_INTERVAL_SECONDS = 3;
    const int MAX_ACTIVITY_ROWS = 80;
    const int BODY_PREVIEW_CHARS = 220;

    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly string _orchId;
    readonly DispatcherTimer _refreshTimer;

    public OrchestrationDetailWindow(ISupervisionPaths paths, IOrchestrationSessionStore store, string orchId)
    {
        _paths = paths;
        _store = store;
        _orchId = orchId;

        InitializeComponent();
        DarkTitleBar_Enabler.Apply(this);

        Refresh_All();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(REFRESH_INTERVAL_SECONDS) };
        _refreshTimer.Tick += (_, _) => Refresh_All();
        _refreshTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _refreshTimer.Stop();
        base.OnClosed(e);
    }

    void Refresh_All()
    {
        try
        {
            var session = _store.Get_Session_OrNull(_orchId);

            if (session == null)
            {
                TitleText.Text = _orchId;
                RepoText.Text = "session.json not found — the orchestration folder may have been removed";
                return;
            }

            Refresh_Header(session);
            Refresh_Ledger(session);
            Refresh_Activity(session);
            MembersItemsControl.ItemsSource = SessionRows_Builder.Build_AllRows(_paths, Find_Brush, session);
            RefreshedText.Text = $"refreshed {DateTime.Now:HH:mm:ss} · auto every {REFRESH_INTERVAL_SECONDS}s";
        }
        catch (Exception ex)
        {
            RefreshedText.Text = $"refresh failed: {ex.Message}";
        }
    }

    void Refresh_Header(IOrchestrationSession session)
    {
        Title = $"{session.DisplayName ?? session.OrchId} — AI Orchestrator";
        TitleText.Text = session.DisplayName ?? session.OrchId;
        OrchIdText.Text = session.OrchId;
        ClosedText.Text = session.ClosedUtc == null ? "" : $"CLOSED {session.ClosedUtc.Value.ToLocalTime():dd/MM HH:mm}";
        RepoText.Text = $"{session.RepoName}  ·  {session.RepoPath}";

        var openImplementers = session.Members.Count(m => m.ClosedUtc == null);
        var age = (session.ClosedUtc ?? DateTime.UtcNow) - session.CreatedUtc;
        var (cost, tokens) = SessionRows_Builder.Build_UsageTotals(_paths, session);
        var progress = PlanLedger_Parser.Parse_OrNull(SafeFile_Reader.Read_Text_Safe(_paths.Get_PlanFile(session.OrchId)));

        List<StatChipView> chips =
        [
            new StatChipView { Label = "STATE", Value = session.ClosedUtc == null ? "running" : "closed", ValueBrush = Find_Brush(session.ClosedUtc == null ? "StateWorking" : "StateClosed") },
            new StatChipView { Label = session.ClosedUtc == null ? "RUNNING FOR" : "RAN FOR", Value = SessionRows_Builder.Describe_Duration(age), ValueBrush = Find_Brush("TextPrimary") },
            new StatChipView { Label = "IMPLEMENTERS", Value = $"{openImplementers} open / {session.Members.Count} total", ValueBrush = Find_Brush("AccentImplementer") },
        ];

        if (progress != null)
        {
            var percent = progress.Total == 0 ? 0 : progress.Done * 100 / progress.Total;

            chips.Add(new StatChipView { Label = "PROGRESS", Value = $"{percent}%  ({progress.Done}/{progress.Total})", ValueBrush = Find_Brush("AccentCommunicator") });

            if (progress.InProgress > 0)
                chips.Add(new StatChipView { Label = "IN PROGRESS", Value = progress.InProgress.ToString(), ValueBrush = Find_Brush("StateWorking") });

            if (progress.Blocked > 0)
                chips.Add(new StatChipView { Label = "BLOCKED", Value = progress.Blocked.ToString(), ValueBrush = Find_Brush("StateBlocked") });
        }

        if (tokens > 0)
            chips.Add(new StatChipView { Label = "TOKENS (lifetime)", Value = SessionRows_Builder.Format_Tokens(tokens), ValueBrush = Find_Brush("TextPrimary") });

        if (cost > 0)
            chips.Add(new StatChipView { Label = "USAGE (not billed)", Value = $"≈${cost:F2} equiv", ValueBrush = Find_Brush("TextPrimary") });

        chips.Add(new StatChipView { Label = "MODELS", Value = $"sup {session.SupervisorModelOverride ?? "default"} · imp {session.ImplementerModelOverride ?? "default"}", ValueBrush = Find_Brush("TextSecondary") });

        StatsItemsControl.ItemsSource = chips;
    }

    void Refresh_Ledger(IOrchestrationSession session)
    {
        var planText = SafeFile_Reader.Read_Text_Safe(_paths.Get_PlanFile(session.OrchId));
        var progress = PlanLedger_Parser.Parse_OrNull(planText);

        if (progress == null)
        {
            NoLedgerText.Visibility = Visibility.Visible;
            LedgerCountsText.Text = "";
            PlanItemsControl.ItemsSource = null;
            return;
        }

        NoLedgerText.Visibility = Visibility.Collapsed;
        LedgerCountsText.Text = $"{progress.Done}/{progress.Total} done";

        List<PlanLineView> lines = [];

        foreach (var rawLine in planText.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();

            var match = System.Text.RegularExpressions.Regex.Match(line, @"^-\s*\[(x|X| |>|!)\]\s*(.*)$");

            if (!match.Success)
                continue;

            lines.Add(Build_PlanLine(match.Groups[1].Value, match.Groups[2].Value.Trim()));
        }

        PlanItemsControl.ItemsSource = lines;
    }

    PlanLineView Build_PlanLine(string marker, string taskText)
    {
        return marker switch
        {
            "x" or "X" => new PlanLineView { MarkerGlyph = "✔", MarkerBrush = Find_Brush("AccentCommunicator"), TaskText = taskText, LineOpacity = 0.55 },
            ">" => new PlanLineView { MarkerGlyph = "▶", MarkerBrush = Find_Brush("StateWorking"), TaskText = taskText, TaskWeight = FontWeights.Bold },
            "!" => new PlanLineView { MarkerGlyph = "■", MarkerBrush = Find_Brush("StateBlocked"), TaskText = taskText, TaskWeight = FontWeights.Bold },
            " " => new PlanLineView { MarkerGlyph = "○", MarkerBrush = Find_Brush("StateNew"), TaskText = taskText },
            _ => throw new Exception($"Unhandled plan marker '{marker}' for task '{taskText}'"),
        };
    }

    /// <summary>Every channel of the orchestration, merged and newest-first — the full picture in one column.</summary>
    void Refresh_Activity(IOrchestrationSession session)
    {
        List<(DateTime When, ActivityRowView Row)> rows = [];

        Collect_ChannelEntries(_paths.Get_OwnerChannelFile(session.OrchId), "owner", rows);

        foreach (var member in session.Members)
            Collect_ChannelEntries(_paths.Get_ImplementerChannelFile(session.OrchId, member.MemberId), member.MemberId, rows);

        ActivityItemsControl.ItemsSource = rows
            .OrderByDescending(item => item.When)
            .Take(MAX_ACTIVITY_ROWS)
            .Select(item => item.Row)
            .ToList();
    }

    void Collect_ChannelEntries(string channelFile, string sourceLabel, List<(DateTime When, ActivityRowView Row)> rows)
    {
        foreach (var entry in ChannelEntry_Parser.Parse_All(SafeFile_Reader.Read_Text_Safe(channelFile)))
        {
            // Unparsable dates sort oldest rather than dropping the entry — never hide traffic.
            DateTime.TryParse(entry.DateText, out var when);

            rows.Add((when, new ActivityRowView
            {
                TimeText = entry.DateText,
                AuthorLabel = Describe_Author(entry.Author),
                AuthorBrush = Find_Brush(AuthorBrush_KeyFor(entry.Author)),
                SourceLabel = sourceLabel,
                Subject = entry.Subject,
                BodyPreview = Build_BodyPreview(entry),
            }));
        }
    }

    static string Build_BodyPreview(IChannelEntry entry)
    {
        var body = entry.Body.Replace('\n', ' ').Replace('\r', ' ').Trim();

        if (body.Length <= BODY_PREVIEW_CHARS)
            return body;

        return $"{body[..BODY_PREVIEW_CHARS]}…";
    }

    static string Describe_Author(ChannelAuthors author)
    {
        return author switch
        {
            ChannelAuthors.Supervisor => "supervisor",
            ChannelAuthors.Implementer => "implementer",
            ChannelAuthors.Owner => "you",
            ChannelAuthors.App => "app",
            ChannelAuthors.Communicator => "communicator",
            ChannelAuthors.Unknown => "unknown",
            _ => throw new Exception($"Unhandled ChannelAuthors: {author}"),
        };
    }

    static string AuthorBrush_KeyFor(ChannelAuthors author)
    {
        return author switch
        {
            ChannelAuthors.Supervisor => "AccentSupervisor",
            ChannelAuthors.Implementer => "AccentImplementer",
            ChannelAuthors.Owner => "TextPrimary",
            ChannelAuthors.App => "AccentGeneral",
            ChannelAuthors.Communicator => "AccentCommunicator",
            ChannelAuthors.Unknown => "TextSecondary",
            _ => throw new Exception($"Unhandled ChannelAuthors: {author}"),
        };
    }

    Brush Find_Brush(string resourceKey)
    {
        if (FindResource(resourceKey) is Brush brush)
            return brush;

        return Brushes.Gray;
    }

    void ShowSessionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not MemberRowView row)
            return;

        if (row.FocusTitleFragment.Length == 0)
            return;

        var focused = AIOrchestratorCoreLib.WindowFocus.TerminalWindow_Focuser.Try_Focus_ByTitleFragment(row.FocusTitleFragment);

        if (!focused)
            MessageBox.Show($"No terminal window found titled '{row.FocusTitleFragment}' — is the session running?", "AI Orchestrator");
    }

    void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = _paths.Get_OrchestrationFolder(_orchId);

        if (!Directory.Exists(folder))
            return;

        Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
    }

    void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
