namespace AIOrchestratorCoreLib.Web.SettingsWebHost;

/// <summary>
/// THE SETTINGS PAGE'S ONE SOCKET (plan 04 Task 7, spec §8.3): an <c>HttpListener</c> on <c>web.listen</c>
/// that moves bytes between a browser and <see cref="SettingsRequest_Handler"/>, and serves the embedded page
/// (<see cref="SettingsPage_Reader"/>) at <c>GET /</c>. It decides nothing about a setting and nothing about a
/// request's meaning — the handler does — only whether a caller is on this machine, how big a body may be, and
/// what to bind. Built once by the composition root and started by BOTH hosts, the WPF app and the daemon,
/// beside the bridge engine.
///
/// <para>
/// IT NEVER TAKES THE BRIDGE DOWN. "Off", an address it will not bind, a port another host already holds —
/// each is one log line and a <see cref="Run_Async"/> that simply returns, because the bridge beside it is the
/// thing the owner cannot lose, and a settings page is not.
/// </para>
/// </summary>
public interface ISettingsWebHost
{
    /// <summary>
    /// Binds, serves until <paramref name="cancellationToken"/> is cancelled, and returns. Returns at once —
    /// never throwing — when <c>web.listen</c> is off or cannot be bound; the reason is in the log.
    /// </summary>
    Task Run_Async(CancellationToken cancellationToken);

    bool IsListening { get; }

    /// <summary>The first prefix bound (<c>http://127.0.0.1:7391/</c> by default), or null while not listening.</summary>
    string? ListeningOn_OrNull { get; }
}
