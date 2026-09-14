using AIOrchestratorCoreLib.Composition;
using AIOrchestratorCoreLib.Composition.HostOptions;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Composition.OrchestratorServices;
using AIOrchestratorCoreLib.Kit;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Termination;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Systemd;

namespace AIOrchestrator.Daemon;

/// <summary>
/// The daemon's one hosted service. Its start is the WPF OnStartup without the window: instance
/// lock, service graph, kit self-install, engine in the background. Its stop is the WPF OnExit:
/// engine cancelled and awaited, every spawned session killed (decision 8 — sessions die with
/// the host; the watchdog respawns them on the next start), lock released.
///
/// Everything up to the engine start runs SYNCHRONOUSLY inside ExecuteAsync, before its first
/// await: the generic host reports READY=1 to systemd once every hosted service's StartAsync has
/// returned, and a BackgroundService's StartAsync returns at ExecuteAsync's first incomplete await.
/// So READY means the lock is held, the services are built, the kit is installed and the engine
/// task has been STARTED — not that the engine has completed a tick. An engine that throws on its
/// first tick does so after systemd was told the unit is up; the watchdog ping below is what
/// notices that, because it stops the moment the engine task completes.
/// </summary>
sealed class BridgeHost_Service(
    IHostOptions options,
    IHostApplicationLifetime lifetime,
    IServiceProvider serviceProvider) : BackgroundService
{
    public const string SERVICE_NAME = "AIOrchestrator";

    /// <summary>
    /// How long a stopping daemon waits for the engine's loops to finish their tick after the
    /// cancel. The engine's last act is to drain queued Telegram announcements; past this bound
    /// the sessions are killed anyway, because a hung drain must not hold up a service stop.
    /// </summary>
    /// <summary>When the configuration cannot be read: the grace for the default turn timeout (ShutdownGrace_Rule).</summary>
    static readonly TimeSpan ENGINE_STOP_GRACE_FALLBACK = ShutdownGrace_Rule.Compute_EngineStopGrace(ShutdownGrace_Rule.DEFAULT_LONGEST_TURN_TIMEOUT);

    readonly IHostOptions _options = options;
    readonly IHostApplicationLifetime _lifetime = lifetime;
    readonly IServiceProvider _serviceProvider = serviceProvider;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // EVERYTHING IS INSIDE THIS GUARD, and the exit code is the whole point. A BackgroundService
        // that faults stops the host and the process still exits 0 (measured on
        // Microsoft.Extensions.Hosting 10.0.11) — so `Restart=on-failure`, launchd's
        // KeepAlive/SuccessfulExit=false and `sc failure` all read a clean stop and do NOTHING. The
        // bridge would be dead and stay dead, which is the one outcome every one of those three
        // service definitions was written to prevent.
        try
        {
            await Run_Host_Async(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The ordinary stop.
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"AI Orchestrator daemon failed: {exception}");
            Environment.ExitCode = 1;
            _lifetime.StopApplication();
        }
    }

    async Task Run_Host_Async(CancellationToken stoppingToken)
    {
        var paths = SupervisionPaths_Factory.Create(_options.SupervisionRoot);
        using var instanceLock = SingleInstance_Guard.Try_Acquire(paths, out var lockFailure);

        if (instanceLock == null)
        {
            // The reason carries its own explanation — a path that cannot be created must not be
            // explained with "only one host may run", which is an answer to a different question.
            Console.Error.WriteLine($"AI Orchestrator will not start: {lockFailure}.");
            Environment.ExitCode = 1;
            _lifetime.StopApplication();
            return;
        }

        // EVERY SESSION THIS HOST SPAWNS INHERITS THE ROOT. The role commands compose their channel
        // path from the supervision root, and until --root existed that root was always
        // ~/.claude/supervision, so a literal was correct. It is not any more: measured 2026-09-06,
        // a print-run implementer under `--root /tmp/...` read
        // `$HOME/.claude/supervision/<orch>/<member>/channel.md`, found nothing, and burned a whole
        // turn to the timeout looking for it. Set on THIS process, so both runners are covered at
        // once — Process.Start copies the parent environment, and the print dispatcher's own
        // AIORCH_* dictionary is additive, not a replacement.
        Environment.SetEnvironmentVariable(HostOptions_Factory.SUPERVISION_ROOT_ENV, paths.Root);

        var services = OrchestratorServices_Factory.Create(paths);
        var forSystemdJournal = SystemdHelpers.IsSystemdService();
        var consoleWriter = new ConsoleLog_Writer(forSystemdJournal);
        services.Log.EntryLogged += consoleWriter.On_EntryLogged;

        services.Engine.MutedChanged += muted => services.Log.Log_Info("", muted ? "Telegram muted (🌙 do-not-disturb on)" : "Telegram unmuted (🌙 off)");
        services.Engine.SilenceAllChanged += silenced => services.Log.Log_Info("", silenced ? "All topics silenced (🔕 on)" : "Topics audible again (🔕 off)");

        services.Log.Log_Info("", $"Daemon starting — supervision root {paths.Root}, Claude home {_options.ClaudeHome}");

        // SAID AT STARTUP, WHERE AN OPERATOR READS IT. The host's shutdown timeout is fixed in
        // Program.cs for the default turn timeout; a configuration that raised the turn timeout would
        // have its drain cut again, silently, exactly like 2026-09-09. Warned, never refused: a daemon
        // that will not start over a config value takes every session down with it.
        var mismatch = ShutdownGrace_Rule.Describe_Mismatch_OrNull(TurnTimeout_Rule.Resolve_Longest(services.ConfigProvider.Get_Current().Runners), ShutdownGrace_Rule.HOST_SHUTDOWN_TIMEOUT);

        if (mismatch != null)
            services.Log.Log_Warning("", $"Shutdown grace: {mismatch}");
        services.Log.Log_Info("", services.ConfigProvider.Get_Current().Is_TelegramConfigured()
            ? "Telegram: mirror + remote input active"
            : "Telegram: not configured (file-only mode) — fill config.json and secrets.json, then restart");

        KitAssets_Bootstrapper.Ensure_Installed(Path.Combine(AppContext.BaseDirectory, "kit"), _options.ClaudeHome, paths, services.Log, services.PluginGate);

        using var engineCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var engineTask = Task.Run(() => services.Engine.Run_Async(engineCancellation.Token), CancellationToken.None);

        try
        {
            await Keep_SystemdWatchdogFed_Async(engineTask, stoppingToken);
        }
        finally
        {
            services.Log.Log_Info("", "Daemon stopping — cancelling the bridge engine");
            engineCancellation.Cancel();
            await Await_EngineStop_Async(engineTask, services);

            // Every spawned session (general + supervisors + implementers) dies with the host.
            // Orchestration state survives on disk; the watchdog respawns everything (with resume
            // semantics) on the next start.
            SessionTerminator.Kill_AllSessions(paths);
            services.Log.Log_Info("", "Daemon stopped — sessions terminated, instance lock released");
            services.Log.EntryLogged -= consoleWriter.On_EntryLogged;
        }
    }

    /// <summary>
    /// The daemon's main loop: as long as the engine is running, tell systemd so. The ping is
    /// sent from HERE, the hosted service's own loop, and it stops the moment the engine task
    /// completes — an engine that died with the process still alive is exactly the state the
    /// watchdog exists to catch, and a ping from an independent timer would hide it. Outside
    /// systemd (launchd, Windows, a terminal) there is no notifier and this simply waits.
    /// </summary>
    async Task Keep_SystemdWatchdogFed_Async(Task engineTask, CancellationToken stoppingToken)
    {
        var notifier = _serviceProvider.GetService<ISystemdNotifier>();
        var pingInterval = SystemdWatchdog_Schedule.Compute_PingInterval_OrNull(
            Environment.GetEnvironmentVariable(SystemdWatchdog_Schedule.WATCHDOG_USEC_ENV));

        if (notifier == null || !notifier.IsEnabled || pingInterval == null)
        {
            await Wait_ForEngineOrStop_Async(engineTask, stoppingToken);
            return;
        }

        var watchdogPing = new ServiceState("WATCHDOG=1");

        while (!stoppingToken.IsCancellationRequested && !engineTask.IsCompleted)
        {
            notifier.Notify(watchdogPing);

            try
            {
                await Task.Delay(pingInterval.Value, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Stop requested: the finally in ExecuteAsync takes it from here.
            }
        }
    }

    static TimeSpan Compute_EngineStopGrace(IOrchestratorServices services)
    {
        try
        {
            return ShutdownGrace_Rule.Compute_EngineStopGrace(TurnTimeout_Rule.Resolve_Longest(services.ConfigProvider.Get_Current().Runners));
        }
        catch
        {
            return ENGINE_STOP_GRACE_FALLBACK;
        }
    }

    static async Task Wait_ForEngineOrStop_Async(Task engineTask, CancellationToken stoppingToken)
    {
        var stopRequested = new TaskCompletionSource();
        using var registration = stoppingToken.Register(() => stopRequested.TrySetResult());

        await Task.WhenAny(engineTask, stopRequested.Task);
    }

    /// <summary>
    /// An engine that ENDED on its own while the host still runs is a bug the WPF app could not
    /// see either: recorded as an error and the host stops, so the init system restarts it
    /// (Restart=on-failure / KeepAlive) rather than leaving a live process with a dead bridge.
    /// </summary>
    async Task Await_EngineStop_Async(Task engineTask, IOrchestratorServices services)
    {
        // THE ENGINE'S LAST ACT IS THE DISPATCHER'S DRAIN — in-flight turns run to their end, up to the
        // turn timeout plus a minute (PrintTurnDispatcherModel.Stop_Async). A grace shorter than that
        // would kill exactly the work the drain exists to keep: measured 2026-09-06→08, 17 turns and
        // 112 M tokens died within four minutes of a `Daemon stopping` line. The init system's own stop
        // timeout has to agree (deploy/systemd: TimeoutStopSec) or it SIGKILLs first.
        var grace = Compute_EngineStopGrace(services);

        try
        {
            await engineTask.WaitAsync(grace);
        }
        catch (OperationCanceledException)
        {
            // The ordinary way Run_Async ends after a cancel.
        }
        catch (TimeoutException)
        {
            services.Log.Log_Warning("", $"Bridge engine did not stop within {grace.TotalSeconds:0} s — proceeding with session termination");
        }
        catch (Exception exception)
        {
            services.Log.Log_Error("", "Bridge engine ended with an error", exception);
            Environment.ExitCode = 1;
        }

        if (engineTask.IsCompleted && !_lifetime.ApplicationStopping.IsCancellationRequested)
        {
            services.Log.Log_Error("", "Bridge engine ended while the daemon was still running — stopping so the service manager restarts it", null);
            Environment.ExitCode = 1;
            _lifetime.StopApplication();
        }
    }
}
