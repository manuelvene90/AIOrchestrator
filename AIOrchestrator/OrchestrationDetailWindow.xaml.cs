using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AIOrchestrator.Views;
using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Planning.PlanProgress;
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

    /// <summary>git shells out per tree — cheap, but not every 3 seconds.</summary>
    const int GIT_REFRESH_INTERVAL_SECONDS = 20;

    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IBridgeEngine _engine;
    readonly string _orchId;
    readonly DispatcherTimer _refreshTimer;

    DateTime _lastGitRefreshUtc = DateTime.MinValue;

    public OrchestrationDetailWindow(ISupervisionPaths paths, IOrchestrationSessionStore store, IBridgeEngine engine, string orchId)
    {
        _paths = paths;
        _store = store;
        _engine = engine;
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

            AddImplementerButton.IsEnabled = session.ClosedUtc == null;
            CloseOrchestrationButton.IsEnabled = session.ClosedUtc == null;

            // Git shells out — refresh it on a slower beat than the file-based panels.
            if ((DateTime.UtcNow - _lastGitRefreshUtc).TotalSeconds >= GIT_REFRESH_INTERVAL_SECONDS)
            {
                Refresh_Git(session);
                _lastGitRefreshUtc = DateTime.UtcNow;
            }

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
            // The percentage comes from the shared rule. The copy here computed the identical
            // expression and so never disagreed with Telegram — a duplicated formula rather than a
            // live defect — but the next edit to either is where a second copy earns its keep.
            chips.Add(new StatChipView { Label = "PROGRESS", Value = $"{PlanProgress_Formatter.Percent(progress)}%  ({progress.Done}/{progress.Total})", ValueBrush = Find_Brush("AccentCommunicator") });

            if (progress.InProgress > 0)
                chips.Add(new StatChipView { Label = "IN PROGRESS", Value = progress.InProgress.ToString(), ValueBrush = Find_Brush("StateWorking") });

            if (progress.Blocked > 0)
                chips.Add(new StatChipView { Label = "BLOCKED", Value = progress.Blocked.ToString(), ValueBrush = Find_Brush("StateBlocked") });

            // A percentage that reached 100% by DROPPING the remainder must say so on the same
            // screen. `IPlanProgress` states the rule in its own words — a marker that removes weight
            // is a delete key unless it is visible — and this chip row showed the percentage without
            // it, which is the same omission as the missing rows arriving through the numbers.
            if (progress.NotDoing > 0)
                chips.Add(new StatChipView { Label = "NOT DOING", Value = progress.NotDoing.ToString(), ValueBrush = Find_Brush("TextSecondary") });
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
        var progress = PlanLedger_Parser.Parse_OrNull(SafeFile_Reader.Read_Text_Safe(_paths.Get_PlanFile(session.OrchId)));

        if (progress == null)
        {
            NoLedgerText.Visibility = Visibility.Visible;
            LedgerCountsText.Text = "";
            PlanItemsControl.ItemsSource = null;
            return;
        }

        NoLedgerText.Visibility = Visibility.Collapsed;

        // THE SHARED WORDING, which carries "· N not doing" — the thing this screen was missing. It
        // said `{Done}/{Total} done` in its own words, so the one surface that shows the ledger in
        // full was also the one that never admitted anything had been dropped from the denominator.
        LedgerCountsText.Text = PlanProgress_Formatter.Describe_Counts(progress);

        // THE SHARED PARSER, for the rows too. This method already called it for the count above and
        // then re-parsed the same text with its own regex — one screen, two parsers, and the local
        // one was missing `[-]`, so every dropped line vanished from the only view that shows the
        // file whole. Elsewhere `[-]` is legitimately invisible because it is out of the denominator;
        // here it is the thing being read, and its absence is unreadable as anything but "that task
        // was never there".
        PlanItemsControl.ItemsSource = PlanLedgerRows_Builder.Build_Rows(progress).Select(Build_PlanLine).ToList();
    }

    /// <summary>
    /// PALETTE LOOKUP AND NOTHING ELSE. Which glyph a marker gets, how dim a dropped line is, and
    /// what stops loudly all moved to `PlanLedgerRows_Builder`, because the test project cannot
    /// reference this one — so every decision left in here is unpinnable by construction (rev-7 L1).
    /// What remains is the part that genuinely cannot leave: resolving a resource key to a Brush.
    /// </summary>
    PlanLineView Build_PlanLine(PlanLedgerRow row)
    {
        return new PlanLineView
        {
            MarkerGlyph = row.Glyph,
            MarkerBrush = Find_Brush(row.BrushKey),
            TaskText = row.Text,
            LineOpacity = row.Opacity,
            TaskWeight = row.IsBold ? FontWeights.Bold : FontWeights.Normal,
        };
    }

    /// <summary>Every channel of the orchestration, merged and newest-first — the full picture in one column.</summary>
    void Refresh_Activity(IOrchestrationSession session)
    {
        List<(DateTime When, ActivityRowView Row)> rows = [];

        Collect_ChannelEntries(_paths.Get_OwnerChannelFile(session.OrchId), "owner", rows);

        foreach (var member in session.Members)
            Collect_ChannelEntries(AIOrchestratorCoreLib.Channels.MemberChannel_Locator.Get_ChannelFile(_paths, session.OrchId, member.MemberId), member.MemberId, rows);

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
            ChannelAuthors.Reviewer => "reviewer",
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
            ChannelAuthors.Reviewer => "AccentReviewer",
            ChannelAuthors.Owner => "TextPrimary",
            ChannelAuthors.App => "AccentGeneral",
            ChannelAuthors.Communicator => "AccentCommunicator",
            ChannelAuthors.Unknown => "TextSecondary",
            _ => throw new Exception($"Unhandled ChannelAuthors: {author}"),
        };
    }

    /// <summary>What the repo and its worktrees ACTUALLY contain — independent of what agents report.</summary>
    void Refresh_Git(IOrchestrationSession session)
    {
        List<GitTreeView> trees = [];

        foreach (var snapshot in AIOrchestratorCoreLib.Git.GitSnapshot_Reader.Read_RepoAndWorktrees(session.RepoPath))
        {
            if (!snapshot.IsRepository)
                continue;

            List<string> stateParts = [];

            if (snapshot.AheadOfUpstream > 0)
                stateParts.Add($"{snapshot.AheadOfUpstream} ahead");

            if (snapshot.BehindUpstream > 0)
                stateParts.Add($"{snapshot.BehindUpstream} behind");

            stateParts.Add(snapshot.DirtyFileCount > 0 ? $"{snapshot.DirtyFileCount} uncommitted" : "clean");

            trees.Add(new GitTreeView
            {
                HeaderText = $"{snapshot.ShortPath}  [{snapshot.Branch}]",
                StateText = string.Join(" · ", stateParts),
                StateBrush = Find_Brush(snapshot.DirtyFileCount > 0 ? "StateAwaitingReview" : "AccentCommunicator"),
                CommitsText = string.Join('\n', snapshot.RecentCommits.Take(5)),
            });
        }

        GitItemsControl.ItemsSource = trees;
    }

    void AddImplementerButton_Click(object sender, RoutedEventArgs e)
    {
        Drop_Request(
            $$"""{"action":"add-implementer","orchId":"{{_orchId}}","reason":"spawned by the owner from the detail window"}""",
            "add-implementer requested");
    }

    void AddReviewerButton_Click(object sender, RoutedEventArgs e)
    {
        Drop_Request(
            $$"""{"action":"add-reviewer","orchId":"{{_orchId}}","reason":"spawned by the owner from the detail window"}""",
            "add-reviewer requested");
    }

    void CloseOrchestrationButton_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            $"Close orchestration '{_orchId}'?\n\nEvery session ends, the Telegram topic is deleted, and the folder stays on disk as audit trail.",
            "AI Orchestrator",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
            return;

        // Straight to the engine, NOT through a request file — see MainWindow for why.
        try
        {
            _engine.Close_Orchestration_ByOwner(_orchId, "closed by the owner");
            RefreshedText.Text = "closed by the owner";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Close failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Spawning actions go through the SAME request-file protocol the agents use. Closing does NOT:
    /// that one is held for the owner's tap when an agent asks, so the owner's own close calls the
    /// engine directly rather than writing a file that would have to claim it was already approved.
    /// </summary>
    void Drop_Request(string json, string confirmation)
    {
        try
        {
            Directory.CreateDirectory(_paths.RequestsFolder);
            File.WriteAllText(Path.Combine(_paths.RequestsFolder, $"ui-{_orchId}-{DateTime.UtcNow.Ticks}.json"), json);
            RefreshedText.Text = $"{confirmation} — the app executes it within a couple of seconds";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Request failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ONE resolver, and it cannot raise. See Brush_Resolver: FindResource THROWS on a missing key,
    // so the Gray fallback never covered the case it appeared to.
    Brush Find_Brush(string resourceKey)
    {
        return Views.Brush_Resolver.Find_OrFallback(this, resourceKey);
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
