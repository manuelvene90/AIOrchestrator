using System.IO;
using System.Windows;
using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Kit;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.Spawning.SessionSpawner;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Termination;

namespace AIOrchestrator;

/// <summary>
/// Composition root. Builds the CoreLib services, enforces single instance (the bridge's
/// getUpdates long-poll only tolerates ONE consumer per bot token), starts the bridge engine
/// in the background and opens the main window.
/// </summary>
public partial class App : Application
{
    const string SINGLE_INSTANCE_MUTEX_NAME = "AIOrchestrator_SingleInstance";

    Mutex? _singleInstanceMutex;
    CancellationTokenSource? _engineCancellation;
    ISupervisionPaths? _paths;
    AIOrchestratorCoreLib.Logging.OrchestrationLog.IOrchestrationLog? _log;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SINGLE_INSTANCE_MUTEX_NAME, out var createdNew);

        if (!createdNew)
        {
            MessageBox.Show(
                "AI Orchestrator is already running. Only one instance may run (the Telegram bridge allows a single poller).",
                "AI Orchestrator",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var paths = SupervisionPaths_Factory.Create_Default();
        _paths = paths;
        var configProvider = OrchestratorConfigProvider_Factory.Create(paths);
        var log = OrchestrationLog_Factory.Create(paths);
        _log = log;

        Install_GlobalExceptionHandlers();
        Ensure_KitAssetsInstalled(paths, log);
        var store = OrchestrationSessionStore_Factory.Create(paths);
        var spawner = SessionSpawner_Factory.Create();
        var launcher = OrchestrationLauncher_Factory.Create(paths, configProvider, store, spawner, log);
        var engine = BridgeEngine_Factory.Create(paths, configProvider, store, launcher, log);

        _engineCancellation = new CancellationTokenSource();
        var engineToken = _engineCancellation.Token;
        _ = Task.Run(() => engine.Run_Async(engineToken), engineToken);

        var mainWindow = new MainWindow(paths, configProvider, store, launcher, engine, log);
        mainWindow.Show();
    }

    /// <summary>
    /// An unhandled exception must NEVER take the app down — the app dying kills every agent
    /// session with it. UI-thread exceptions are logged, shown, and marked handled; background
    /// task exceptions are logged and observed. (The engine's own loops already catch per-tick.)
    /// </summary>
    void Install_GlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            _log?.Log_Error("", "Unhandled UI exception — app kept alive", args.Exception);
            MessageBox.Show(
                $"An internal error occurred and was contained (the app and all sessions keep running):\n\n{args.Exception.Message}",
                "AI Orchestrator",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _log?.Log_Error("", "Unobserved background task exception — app kept alive", args.Exception);
            args.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            // Non-recoverable path (runtime is tearing down) — at least leave a trace.
            _log?.Log_Error("", "FATAL unhandled exception — app is going down", args.ExceptionObject as Exception);
        };
    }

    /// <summary>
    /// Launching the app must be enough: the role commands (/supervisor, /implementer,
    /// /general-supervisor) and the status line script self-install/refresh from the kit shipped
    /// in the app's output folder — no install.ps1 prerequisite for them.
    /// </summary>
    static void Ensure_KitAssetsInstalled(ISupervisionPaths paths, AIOrchestratorCoreLib.Logging.OrchestrationLog.IOrchestrationLog log)
    {
        try
        {
            var kitCommandsFolder = Path.Combine(AppContext.BaseDirectory, "kit", "commands");
            var kitStatuslineFile = Path.Combine(AppContext.BaseDirectory, "kit", "statusline", "statusline.ps1");
            var claudeCommandsFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "commands");
            var statuslineTargetFile = Path.Combine(paths.Root, "statusline.ps1");

            var installedFiles = KitAssets_Installer.Ensure_Installed(
                kitCommandsFolder, kitStatuslineFile, claudeCommandsFolder, statuslineTargetFile);

            foreach (var installedFile in installedFiles)
                log.Log_Info("", $"Kit asset installed/updated: {installedFile}");

            if (!Directory.Exists(kitCommandsFolder))
                log.Log_Warning("", $"Kit commands folder not found at {kitCommandsFolder} — role commands NOT installed");

            var settingsFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

            if (StatusLineSettings_Wirer.Ensure_Wired(settingsFile, statuslineTargetFile))
                log.Log_Info("", $"Status line wired into {settingsFile} (previous file backed up); active for newly spawned sessions");
        }
        catch (Exception ex)
        {
            log.Log_Error("", "Kit asset self-install failed", ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _engineCancellation?.Cancel();

        // Every spawned session (general + supervisors + implementers) dies with the app.
        // Orchestration state survives on disk; the watchdog respawns everything (with resume
        // semantics) on the next app start.
        if (_paths != null)
            SessionTerminator.Kill_AllSessions(_paths);

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
