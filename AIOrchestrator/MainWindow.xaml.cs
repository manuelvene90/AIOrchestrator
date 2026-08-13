using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AIOrchestrator.Views;
using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Configuration.RepoEntry;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Logging;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using System.Collections.ObjectModel;
using AIOrchestratorCoreLib.Status;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestrator;

public partial class MainWindow : Window
{
    const int REFRESH_INTERVAL_SECONDS = 5;
    const int MAX_LOG_ROWS = 500;

    /// <summary>Open + closed cards combined — open ones always show, newest-closed fill the rest.</summary>
    const int MAX_SESSION_CARDS = 15;

    readonly ObservableCollection<LogRowView> _logRows = [];

    readonly ISupervisionPaths _paths;
    readonly IOrchestratorConfigProvider _configProvider;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;
    readonly DispatcherTimer _refreshTimer;

    public MainWindow(
        ISupervisionPaths paths,
        IOrchestratorConfigProvider configProvider,
        IOrchestrationSessionStore store,
        IOrchestrationLauncher launcher,
        IBridgeEngine engine,
        IOrchestrationLog log)
    {
        _paths = paths;
        _configProvider = configProvider;
        _store = store;
        _launcher = launcher;
        _engine = engine;

        InitializeComponent();
        DarkTitleBar_Enabler.Apply(this);

        ActivityLogListBox.ItemsSource = _logRows;
        RootPathText.Text = paths.Root;
        TelegramStatusText.Text = configProvider.Get_Current().Is_TelegramConfigured()
            ? "Telegram: mirror + remote input active"
            : "Telegram: not configured (file-only mode) — set it up in ⚙ Settings, then restart";

        Refresh_ReposList();

        log.EntryLogged += On_LogEntry;
        engine.OrchestrationActivity += _ => Dispatcher.BeginInvoke(Refresh_Orchestrations);
        // Both app-wide modes are also togglable from Telegram (/dnd_all, /mute_all) and by the
        // owner texting (auto-unmute), so the checkboxes follow the engine rather than own it.
        engine.MutedChanged += muted => Dispatcher.BeginInvoke(() =>
        {
            if (MuteTelegramCheckBox.IsChecked != muted)
                MuteTelegramCheckBox.IsChecked = muted;
        });

        engine.SilenceAllChanged += silenced => Dispatcher.BeginInvoke(() =>
        {
            if (SilenceAllCheckBox.IsChecked != silenced)
                SilenceAllCheckBox.IsChecked = silenced;
        });

        // Persisted, unlike the two modes above — so it starts from config rather than from off.
        ItalianLayerCheckBox.IsChecked = configProvider.Get_Current().TelegramItalianLayer;

        engine.ItalianLayerChanged += enabled => Dispatcher.BeginInvoke(() =>
        {
            if (ItalianLayerCheckBox.IsChecked != enabled)
                ItalianLayerCheckBox.IsChecked = enabled;
        });

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(REFRESH_INTERVAL_SECONDS) };
        _refreshTimer.Tick += (_, _) => Refresh_Orchestrations();
        _refreshTimer.Start();

        Refresh_Orchestrations();
        Schedule_GeneralSupervisorFocus();
    }

    /// <summary>
    /// The watchdog spawns the general supervisor a few seconds after startup — once its window
    /// exists, bring it to the front so the owner can start talking right away.
    /// </summary>
    void Schedule_GeneralSupervisorFocus()
    {
        var attempts = 0;
        var focusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };

        focusTimer.Tick += (_, _) =>
        {
            attempts++;

            var focused = AIOrchestratorCoreLib.WindowFocus.TerminalWindow_Focuser.Try_Focus_ByTitleFragment("GENERAL");

            if (focused || attempts >= 5)
                focusTimer.Stop();
        };

        focusTimer.Start();
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

    /// <summary>Closing the app kills EVERY session — never let one accidental X do that silently.</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        var answer = MessageBox.Show(
            "Closing the orchestrator closes EVERY session — the general supervisor and all orchestration supervisors and implementers (their state survives on disk and everything respawns on the next start).\n\nClose anyway?",
            "AI Orchestrator",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        _logRows.Clear();
    }

    void ActivityLogListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ActivityLogListBox.SelectedItem is not LogRowView row)
            return;

        try
        {
            Clipboard.SetText($"{row.TimeText}  {row.Message}");
            Show_CopiedFeedback();
        }
        catch (Exception ex)
        {
            Add_LogRow(LogLevels.Warning, $"Clipboard copy failed: {ex.Message}");
        }
    }

    void Show_CopiedFeedback()
    {
        CopiedFeedbackText.Visibility = Visibility.Visible;

        var hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        hideTimer.Tick += (_, _) =>
        {
            hideTimer.Stop();
            CopiedFeedbackText.Visibility = Visibility.Collapsed;
        };
        hideTimer.Start();
    }

    /// <summary>
    /// config.json changes at RUNTIME (the general supervisor seeds repos, Settings saves) —
    /// the provider returns the same instance until the file changes, so reference equality is
    /// the cheap change check.
    /// </summary>
    void Refresh_ReposList()
    {
        var currentRepos = _configProvider.Get_Current().Repos;

        if (!ReferenceEquals(ReposListBox.ItemsSource, currentRepos))
            ReposListBox.ItemsSource = currentRepos;
    }

    void Refresh_Orchestrations()
    {
        try
        {
            Refresh_ReposList();

            List<OrchestrationCardView> cards = [];

            var generalCard = Build_GeneralCard_OrNull();
            if (generalCard != null)
                cards.Add(generalCard);

            // Open cards first; recently-closed ones stay visible (disabled) until the combined
            // list would exceed the cap — the oldest closed drop off little by little.
            var allSessions = _store.Load_All();
            var openSessions = allSessions.Where(s => s.ClosedUtc == null).OrderByDescending(s => s.CreatedUtc).ToList();
            var closedSessions = allSessions.Where(s => s.ClosedUtc != null).OrderByDescending(s => s.ClosedUtc).ToList();

            foreach (var session in openSessions)
                cards.Add(Build_Card(session));

            var closedSlots = Math.Max(0, MAX_SESSION_CARDS - openSessions.Count);

            foreach (var session in closedSessions.Take(closedSlots))
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
            OrchId = ChannelDiscovery.GENERAL_ORCH_ID,
            Title = "general supervisor",
            RepoName = "always-on · General topic",
            Members = [row],
            OrchestrationButtonsVisibility = Visibility.Collapsed,
            GeneralButtonsVisibility = Visibility.Visible,
        };
    }

    /// <summary>
    /// "2 implementers · 1 reviewer" — reviewers are counted SEPARATELY because they cost the owner
    /// tokens without producing code, so a card that hides them behind an implementer count lies
    /// about where the spend is going.
    /// </summary>
    static string Describe_OpenMembers(IOrchestrationSession session)
    {
        var implementers = 0;
        var reviewers = 0;

        foreach (var member in session.Members)
        {
            if (member.ClosedUtc != null)
                continue;

            if (MemberKind_Ids.Resolve_Kind(member.MemberId) == MemberKinds.Reviewer)
                reviewers++;
            else
                implementers++;
        }

        var text = $"{implementers} implementer{(implementers == 1 ? "" : "s")}";

        if (reviewers > 0)
            text += $" · {reviewers} reviewer{(reviewers == 1 ? "" : "s")}";

        return text;
    }

    OrchestrationCardView Build_Card(IOrchestrationSession session)
    {
        // Closed implementers keep their row (disabled, no Show button) — their channel and usage
        // stay on disk, so the chips and totals remain meaningful. Row shaping is shared with the
        // detail window (SessionRows_Builder) so the two views can never drift apart.
        var rows = SessionRows_Builder.Build_AllRows(_paths, Find_Brush, session);

        var age = (session.ClosedUtc ?? DateTime.UtcNow) - session.CreatedUtc;
        var ranWord = session.ClosedUtc == null ? "running" : "ran";
        var orchIdSuffix = session.DisplayName == null ? "" : $"· {session.OrchId} ";

        var progress = AIOrchestratorCoreLib.Planning.PlanLedger_Parser.Parse_OrNull(
            Read_FileText_Safe(_paths.Get_PlanFile(session.OrchId)));

        return new OrchestrationCardView
        {
            OrchId = session.OrchId,
            Title = session.DisplayName ?? session.OrchId,
            RepoName = session.RepoName,

            // A basic orchestration has no supervisor, so an implementer added here would wait for a
            // brief that cannot come. The launcher refuses it either way — this stops the owner being
            // offered it at all.
            AddMemberButtonVisibility = AIOrchestratorCoreLib.Sessions.OrchestrationShape.Is_BasicOrchestration(session.SupervisorSpawnedUtc)
                ? Visibility.Collapsed
                : Visibility.Visible,
            SummaryText = $"{orchIdSuffix}· {Describe_OpenMembers(session)} · {ranWord} {Describe_Duration(age)}{Build_UsageTotalText(session)}",
            IsClosed = session.ClosedUtc != null,
            ClosedLabel = session.ClosedUtc == null ? "" : $"CLOSED {session.ClosedUtc.Value.ToLocalTime():dd/MM HH:mm}",
            CardOpacity = session.ClosedUtc == null ? 1.0 : 0.5,
            Members = rows,
            SilenceGlyph = Describe_ModeGlyph(session.TelegramMode),
            SilenceTooltip = Describe_ModeTooltip(session.TelegramMode),
            ProgressVisibility = progress == null ? Visibility.Collapsed : Visibility.Visible,
            ProgressText = progress == null ? "" : Build_ProgressText(progress),
            ProgressDoneStar = new GridLength(progress?.Done ?? 0, GridUnitType.Star),
            ProgressLeftStar = new GridLength(Math.Max(0, (progress?.Total ?? 1) - (progress?.Done ?? 0)), GridUnitType.Star),
        };
    }

    static string Build_ProgressText(AIOrchestratorCoreLib.Planning.PlanProgress.IPlanProgress progress)
    {
        List<string> parts = [$"{progress.Done}/{progress.Total} tasks"];

        if (progress.InProgress > 0)
            parts.Add($"{progress.InProgress} running");

        if (progress.Blocked > 0)
            parts.Add($"{progress.Blocked} BLOCKED");

        if (progress.CurrentTaskText != null)
            parts.Add($"now: {progress.CurrentTaskText}");

        return string.Join("  ·  ", parts);
    }

    static string Describe_Duration(TimeSpan duration)
    {
        return SessionRows_Builder.Describe_Duration(duration);
    }

    /// <summary>Orchestration-wide LIFETIME usage (supervisor + communicator + every member, closed included).</summary>
    string Build_UsageTotalText(IOrchestrationSession session)
    {
        var (costTotal, tokenTotal) = SessionRows_Builder.Build_UsageTotals(_paths, session);

        if (costTotal <= 0 && tokenTotal <= 0)
            return "";

        var tokenPart = tokenTotal > 0 ? $" · Σ {SessionRows_Builder.Format_Tokens(tokenTotal)}" : "";
        var costPart = costTotal > 0 ? $" · Σ ≈${costTotal:F2} equiv" : "";

        return $"{tokenPart}{costPart}";
    }

    // ONE resolver, and it cannot raise. See Brush_Resolver: FindResource THROWS on a missing key,
    // so the Gray fallback never covered the case it appeared to.
    Brush Find_Brush(string resourceKey)
    {
        return Views.Brush_Resolver.Find_OrFallback(this, resourceKey);
    }

    static string Read_FileText_Safe(string filePath)
    {
        return SafeFile_Reader.Read_Text_Safe(filePath);
    }

    static string Get_LastWriteText(string filePath)
    {
        return SessionRows_Builder.Get_LastWriteText(filePath);
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

    /// <summary>
    /// The other way to start work: ONE session talking straight to the owner, no supervisor and no
    /// gates. For endeavours where the orchestration apparatus costs more than the work.
    /// </summary>
    void StartBasicButton_Click(object sender, RoutedEventArgs e)
    {
        if (ReposListBox.SelectedItem is not IRepoEntry repo)
        {
            MessageBox.Show("Select a repository first.", "AI Orchestrator");
            return;
        }

        try
        {
            _launcher.Start_BasicOrchestration(repo.Name, repo.Path);
            Refresh_Orchestrations();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Start basic session failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_paths, _configProvider.Get_Current()) { Owner = this };
        settingsWindow.ShowDialog();
        Refresh_ReposList();
    }

    void MuteTelegramCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _engine.Set_TelegramMuted(MuteTelegramCheckBox.IsChecked == true);
    }

    void SilenceAllCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _engine.Set_SilenceAllTopics(SilenceAllCheckBox.IsChecked == true);
    }

    void ItalianLayerCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _engine.Set_ItalianLayer(ItalianLayerCheckBox.IsChecked == true);
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

    /// <summary>
    /// Goes through the same request-file path the general supervisor uses — ONE close code path
    /// (kill sessions, close topic, FROM app confirmation), executed by the engine within ~2 s.
    /// </summary>
    void CloseOrchestrationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not OrchestrationCardView card)
            return;

        var answer = MessageBox.Show(
            $"Close orchestration '{card.Title}'?\n\nIts supervisor and implementer sessions end, its Telegram topic is deleted, and its folder stays on disk as audit trail.",
            "AI Orchestrator",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            // Straight to the engine, NOT through a request file. The request protocol is the AGENT
            // path, where every close is held for the owner's tap; the modal above already IS their
            // confirmation, and a flag in a JSON file claiming so is a lie waiting to be told by
            // accident.
            _engine.Close_Orchestration_ByOwner(card.OrchId, "closed by the owner from the app");
            Add_LogRow(LogLevels.Info, $"[{card.OrchId}] closed by the owner from the UI");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Close failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Clicking anywhere on a card (buttons excepted — they mark the event handled) opens its detail view.</summary>
    void OrchestrationCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not OrchestrationCardView card)
            return;

        Open_DetailWindow(card);
    }

    static string Describe_ModeGlyph(TelegramDeliveryModes mode)
    {
        return mode switch
        {
            TelegramDeliveryModes.Normal => "🔔",
            TelegramDeliveryModes.Deferred => TelegramDeliveryMode_Glyphs.DEFERRED,
            TelegramDeliveryModes.Silenced => TelegramDeliveryMode_Glyphs.SILENCED,
            _ => throw new Exception($"Unhandled TelegramDeliveryModes: {mode}"),
        };
    }

    static string Describe_ModeTooltip(TelegramDeliveryModes mode)
    {
        return mode switch
        {
            TelegramDeliveryModes.Normal => "Messages ON for this topic. Click to SILENCE it (messages dropped — you're in its terminal).",
            TelegramDeliveryModes.Silenced => "SILENCED: this topic's messages are dropped. Click for Do-Not-Disturb (kept and replayed later).",
            TelegramDeliveryModes.Deferred => "DO-NOT-DISTURB: this topic's messages are held and replayed. Click to turn messages back ON.",
            _ => throw new Exception($"Unhandled TelegramDeliveryModes: {mode}"),
        };
    }

    /// <summary>
    /// Cycles this topic: ON → 🔕 silenced (dropped, "I'm in its terminal") → 🌙 deferred (held and
    /// replayed, "I'm away") → ON. The app-wide equivalents are the status-bar checkbox and the
    /// /mute_all and /dnd_all commands.
    /// </summary>
    void SilenceButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not OrchestrationCardView card)
            return;

        try
        {
            var session = _store.Get_Session_OrNull(card.OrchId);

            if (session == null)
                return;

            var nextMode = session.TelegramMode switch
            {
                TelegramDeliveryModes.Normal => TelegramDeliveryModes.Silenced,
                TelegramDeliveryModes.Silenced => TelegramDeliveryModes.Deferred,
                TelegramDeliveryModes.Deferred => TelegramDeliveryModes.Normal,
                _ => throw new Exception($"Unhandled TelegramDeliveryModes: {session.TelegramMode}"),
            };

            _store.Set_TelegramMode(card.OrchId, nextMode);
            Add_LogRow(LogLevels.Info, $"[{card.OrchId}] topic delivery mode → {nextMode}");
            Refresh_Orchestrations();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Delivery mode toggle failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Tiles this orchestration's WORKING terminals across the screen: the supervisor and every
    /// open implementer. The communicator is deliberately excluded — it narrates, it is not work
    /// to watch — and so is this app's own window, which the tiles are laid out around.
    /// </summary>
    void OrganizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not OrchestrationCardView card)
            return;

        try
        {
            var session = _store.Get_Session_OrNull(card.OrchId);

            if (session == null)
                return;

            List<string> titleFragments = [$"SUP · {session.OrchId}"];

            foreach (var member in session.Members)
            {
                if (member.ClosedUtc == null)
                    titleFragments.Add($"{member.MemberId.ToUpperInvariant()} · {session.OrchId}");
            }

            Tile_Terminals(titleFragments, card.OrchId);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Organize failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// The general supervisor's own Organize: one tile per OPEN orchestration's SUPERVISOR, so the
    /// owner sees every supervisor at work in a single glance. The general supervisor's own
    /// terminal is excluded on purpose — it is the window you are organizing FROM, and tiling it
    /// away is the one thing that would make the view less useful.
    /// </summary>
    void OrganizeSupervisorsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            List<string> titleFragments = [];

            foreach (var session in _store.Load_All())
            {
                if (session.ClosedUtc == null)
                    titleFragments.Add($"SUP · {session.OrchId}");
            }

            Tile_Terminals(titleFragments, ChannelDiscovery.GENERAL_ORCH_ID);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Organize failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Shared by both Organize buttons, so a fix to the tiling can never apply to only one.</summary>
    void Tile_Terminals(IReadOnlyList<string> titleFragments, string logContext)
    {
        // Only windows that actually EXIST get a tile, so the layout is computed for exactly the
        // set about to be shown. This used to test focusability instead: Windows refuses
        // SetForegroundWindow in several ordinary situations, so a perfectly real terminal could
        // fail the test, still be sitting on screen, and be left out of the arrangement.
        var living = titleFragments
            .Where(AIOrchestratorCoreLib.WindowFocus.TerminalWindow_Focuser.Exists_ByTitleFragment)
            .ToList();

        if (living.Count == 0)
        {
            Add_LogRow(LogLevels.Warning, $"[{logContext}] Organize: no terminal windows found");
            return;
        }

        var area = SystemParameters.WorkArea;

        var tiles = AIOrchestratorCoreLib.Layout.TileLayout_Calculator.Build_Tiles(
            living.Count, (int)area.Left, (int)area.Top, (int)area.Width, (int)area.Height);

        for (var i = 0; i < living.Count && i < tiles.Count; i++)
        {
            AIOrchestratorCoreLib.WindowFocus.TerminalWindow_Focuser.Try_PlaceWindow_ByTitleFragment(living[i], tiles[i].X, tiles[i].Y, tiles[i].Width, tiles[i].Height);

            // Raise it AFTER placing. The old code raised windows while deciding which existed,
            // so removing that from the filter would otherwise have left them tiled but buried.
            AIOrchestratorCoreLib.WindowFocus.TerminalWindow_Focuser.Try_Focus_ByTitleFragment(living[i]);
        }

        Add_LogRow(LogLevels.Info, $"[{logContext}] Organize: tiled {living.Count} terminal(s)");
    }

    void DetailsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not OrchestrationCardView card)
            return;

        Open_DetailWindow(card);
    }

    void Open_DetailWindow(OrchestrationCardView card)
    {
        // The general supervisor has no orchestration folder, ledger or implementers to detail.
        if (card.OrchId == ChannelDiscovery.GENERAL_ORCH_ID)
            return;

        try
        {
            var detailWindow = new OrchestrationDetailWindow(_paths, _store, _engine, card.OrchId) { Owner = this };
            detailWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Detail view failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not OrchestrationCardView card)
            return;

        var folder = card.OrchId == ChannelDiscovery.GENERAL_ORCH_ID
            ? _paths.GeneralFolder
            : _paths.Get_OrchestrationFolder(card.OrchId);

        Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
    }

    // ---- Repo list drag/drop reorder (persisted into config.json's repos array) ----

    IRepoEntry? _draggedRepo;
    System.Windows.Point _dragStartPoint;

    void ReposListBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _draggedRepo = Get_RepoAtPosition_OrNull(e.GetPosition(ReposListBox));
    }

    void ReposListBox_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || _draggedRepo == null)
            return;

        var moved = e.GetPosition(null) - _dragStartPoint;

        var isRealDrag = Math.Abs(moved.X) >= SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(moved.Y) >= SystemParameters.MinimumVerticalDragDistance;

        if (!isRealDrag)
            return;

        // The dragged repo travels in the _draggedRepo field (the DataObject only carries the
        // name for completeness) — Drop fires synchronously inside this call.
        DragDrop.DoDragDrop(ReposListBox, _draggedRepo.Name, DragDropEffects.Move);
        _draggedRepo = null;
    }

    void ReposListBox_Drop(object sender, DragEventArgs e)
    {
        var draggedRepo = _draggedRepo;

        if (draggedRepo == null)
            return;

        try
        {
            var targetRepo = Get_RepoAtPosition_OrNull(e.GetPosition(ReposListBox));

            if (ReferenceEquals(targetRepo, draggedRepo))
                return;

            List<IRepoEntry> reordered = [.. _configProvider.Get_Current().Repos];
            reordered.Remove(draggedRepo);

            // No row under the drop point = dropped in the empty space below the list → the end.
            var insertIndex = targetRepo == null
                ? reordered.Count
                : Math.Min(reordered.Count, Find_RepoIndex(reordered, targetRepo, e.GetPosition(ReposListBox)));

            reordered.Insert(insertIndex, draggedRepo);

            ConfigRepos_Reorderer.Persist_Order(_paths, reordered.Select(r => r.Name).ToList());
            Refresh_ReposList();
        }
        catch (Exception ex)
        {
            Add_LogRow(LogLevels.Warning, $"Repo reorder failed: {ex.Message}");
        }
    }

    /// <summary>Dropping on a row's upper half inserts before it, lower half after it.</summary>
    int Find_RepoIndex(IReadOnlyList<IRepoEntry> reposWithoutDragged, IRepoEntry targetRepo, System.Windows.Point dropPosition)
    {
        var index = 0;

        for (var i = 0; i < reposWithoutDragged.Count; i++)
        {
            if (ReferenceEquals(reposWithoutDragged[i], targetRepo))
            {
                index = i;
                break;
            }
        }

        var container = Get_ContainerOf_OrNull(targetRepo);

        if (container == null)
            return index;

        var positionInTarget = dropPosition.Y - container.TranslatePoint(new System.Windows.Point(0, 0), ReposListBox).Y;

        return positionInTarget > container.ActualHeight / 2 ? index + 1 : index;
    }

    IRepoEntry? Get_RepoAtPosition_OrNull(System.Windows.Point position)
    {
        for (var i = 0; i < ReposListBox.Items.Count; i++)
        {
            if (ReposListBox.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem container)
                continue;

            var topLeft = container.TranslatePoint(new System.Windows.Point(0, 0), ReposListBox);
            var bounds = new Rect(topLeft, new Size(container.ActualWidth, container.ActualHeight));

            if (bounds.Contains(position))
                return ReposListBox.Items[i] as IRepoEntry;
        }

        return null;
    }

    ListBoxItem? Get_ContainerOf_OrNull(IRepoEntry repo)
    {
        return ReposListBox.ItemContainerGenerator.ContainerFromItem(repo) as ListBoxItem;
    }
}
