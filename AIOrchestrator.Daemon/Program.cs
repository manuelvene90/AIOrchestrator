using AIOrchestrator.Daemon;
using AIOrchestratorCoreLib.Composition.HostOptions;
using AIOrchestratorCoreLib.Running;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// The headless host: the same service graph the WPF app builds, run as a daemon.
//   dotnet run --project AIOrchestrator.Daemon [-- --root DIR] [--claude-home DIR]
// Under systemd the unit's Type=notify and WatchdogSec are honoured (READY=1, WATCHDOG=1);
// under launchd it is a plain KeepAlive process; under the Windows SCM it is a service.
// Ctrl-C and SIGTERM both stop it the way closing the WPF window does: engine cancelled,
// every spawned session killed, instance lock released.

var options = HostOptions_Factory.Create_FromArguments(
    args,
    Environment.GetEnvironmentVariable,
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

// THE SAME RULE THE WPF HOST STATES: an unhandled exception must never take the host down quietly,
// because the host dying kills every agent session with it. The WPF app installs three handlers
// (App.xaml.cs) and the daemon had none — an unobserved background task exception left no trace at
// all, in a process whose only window is its log.
TaskScheduler.UnobservedTaskException += (_, unobserved) =>
{
    Console.Error.WriteLine($"Unobserved background task exception — daemon kept alive: {unobserved.Exception}");
    unobserved.SetObserved();
};

AppDomain.CurrentDomain.UnhandledException += (_, fatal) =>
{
    // Non-recoverable (the runtime is tearing down) — at least leave a trace and an exit code the
    // service manager will act on.
    Console.Error.WriteLine($"FATAL unhandled exception — daemon is going down: {fatal.ExceptionObject}");
    Environment.ExitCode = 1;
};

var builder = Host.CreateApplicationBuilder(args);

// The orchestrator's own log goes to stdout through ConsoleLog_Writer; the host's framework
// logger is kept for the lifetime's own problems only, so a start-up failure is never silent.
builder.Logging.SetMinimumLevel(LogLevel.Warning);

// THE HOST MUST NOT CUT THE DRAIN. Measured 2026-09-09 (three restarts): the dispatcher logged
// "draining 2 in-flight turn(s) (up to 31 min)" and systemd logged "Deactivated successfully" 30 s
// later every time — HostOptions.ShutdownTimeout at its default. Sized for the default turn timeout;
// BridgeHost_Service says so at startup if the configured one outgrows it (ShutdownGrace_Rule).
builder.Services.Configure<HostOptions>(hostOptions => hostOptions.ShutdownTimeout = ShutdownGrace_Rule.HOST_SHUTDOWN_TIMEOUT);

builder.Services.AddSystemd();
builder.Services.AddWindowsService(serviceOptions => serviceOptions.ServiceName = BridgeHost_Service.SERVICE_NAME);
builder.Services.AddSingleton(options);
builder.Services.AddHostedService<BridgeHost_Service>();

await builder.Build().RunAsync();

return Environment.ExitCode;
