using System.Windows;
using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration;
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
        var config = OrchestratorConfig_Loader.Load_OrEmpty(paths);
        var log = OrchestrationLog_Factory.Create(paths);
        var store = OrchestrationSessionStore_Factory.Create(paths);
        var spawner = SessionSpawner_Factory.Create();
        var launcher = OrchestrationLauncher_Factory.Create(paths, config, store, spawner, log);
        var engine = BridgeEngine_Factory.Create(paths, config, store, launcher, log);

        _engineCancellation = new CancellationTokenSource();
        var engineToken = _engineCancellation.Token;
        _ = Task.Run(() => engine.Run_Async(engineToken), engineToken);

        var mainWindow = new MainWindow(paths, config, store, launcher, engine, log);
        mainWindow.Show();
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
