using System.IO;
using System.Windows;
using AIOrchestratorCoreLib.Composition;
using AIOrchestratorCoreLib.Composition.HostOptions;
using AIOrchestratorCoreLib.Composition.OrchestratorServices;
using AIOrchestratorCoreLib.Kit;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Termination;

namespace AIOrchestrator;

/// <summary>
/// The WPF host. The service graph itself is built by <see cref="OrchestratorServices_Factory"/>
/// (shared with the headless daemon); this class enforces single instance (the bridge's
/// getUpdates long-poll only tolerates ONE consumer per bot token), starts the bridge engine in
/// the background and opens the main window.
/// </summary>
public partial class App : Application
{
    IDisposable? _instanceLock;
    CancellationTokenSource? _engineCancellation;
    ISupervisionPaths? _paths;
    AIOrchestratorCoreLib.Logging.OrchestrationLog.IOrchestrationLog? _log;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // FIRST, BEFORE ANYTHING IS BUILT. On master only the config provider and the log were
        // constructed ahead of these handlers; extracting the composition root moved four more
        // constructors above them, which widened the window where a throw is an uncontained crash
        // instead of the contained message box this file exists to guarantee.
        Install_GlobalExceptionHandlers();

        var options = HostOptions_Factory.Create_Default();
        var paths = SupervisionPaths_Factory.Create(options.SupervisionRoot);
        _instanceLock = SingleInstance_Guard.Try_Acquire(paths, out var lockFailure);

        if (_instanceLock == null)
        {
            MessageBox.Show(
                $"AI Orchestrator cannot start: {lockFailure}.",
                "AI Orchestrator",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _paths = paths;

        // The sessions this app spawns read their channel path from this variable (see the daemon's
        // note): here it is always the default root, so nothing changes — it is set so the contract
        // is the host's, stated once, rather than a default the role commands are lucky to match.
        Environment.SetEnvironmentVariable(HostOptions_Factory.SUPERVISION_ROOT_ENV, paths.Root);

        var services = OrchestratorServices_Factory.Create(paths);
        _log = services.Log;

        KitAssets_Bootstrapper.Ensure_Installed(Path.Combine(AppContext.BaseDirectory, "kit"), options.ClaudeHome, paths, services.Log, services.PluginGate);

        _engineCancellation = new CancellationTokenSource();
        var engineToken = _engineCancellation.Token;
        _ = Task.Run(() => services.Engine.Run_Async(engineToken), engineToken);

        var mainWindow = new MainWindow(paths, services.ConfigProvider, services.Store, services.Launcher, services.Engine, services.Log);
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

    protected override void OnExit(ExitEventArgs e)
    {
        _engineCancellation?.Cancel();

        // Every spawned session (general + supervisors + implementers) dies with the app.
        // Orchestration state survives on disk; the watchdog respawns everything (with resume
        // semantics) on the next app start.
        if (_paths != null)
            SessionTerminator.Kill_AllSessions(_paths);

        _instanceLock?.Dispose();
        base.OnExit(e);
    }
}
