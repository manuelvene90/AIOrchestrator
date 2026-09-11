namespace AIOrchestratorCoreLib.Hosting.HostWindowing;

/// <summary>
/// WHAT THIS HOST CAN DO WITH WINDOWS ON A SCREEN — focus one, tile them, photograph one — behind an
/// interface, so the bridge asks instead of assuming.
///
/// <para>
/// THE INCIDENT THIS EXISTS FOR. `WindowFocus/` is three static classes of unguarded `user32.dll`,
/// `dwmapi.dll` and `gdi32.dll` P/Invoke, and the engine called them by name. On Linux there is no
/// user32, so `/show` threw `DllNotFoundException` out of the command dispatch and out of the whole
/// inbound batch — and because the offset only advanced at the END of a batch, Telegram re-served
/// every update in it. On 2026-09-08 01:24-01:26Z that replayed one batch four times. A missing OS
/// feature took the owner's messages down with it.
/// </para>
/// <para>
/// SO THE ANSWER TO "CAN YOU?" IS DATA, NOT AN EXCEPTION. <see cref="Is_Supported"/> is asked before
/// the command runs and the owner is told one line; no implementation here throws for being on the
/// wrong OS.
/// </para>
/// <para>
/// A WINDOW IS IDENTIFIED BY A TITLE FRAGMENT — the string the spawner put in the terminal's title —
/// which is why nothing in this interface mentions a handle: a handle is a Win32 concept, and this
/// seam exists precisely so no Win32 concept reaches the engine.
/// </para>
/// </summary>
public interface IHostWindowing
{
    /// <summary>
    /// Whether this host can do any of it. FALSE on Linux and macOS until real implementations
    /// exist — the owner runs the bridge on both (owner, 2026-09-09).
    /// </summary>
    bool Is_Supported { get; }

    /// <summary>
    /// The title fragment of the window the owner would want to look at for this orchestration — its
    /// supervisor, or its solo — or null when none is on screen.
    /// </summary>
    string? Find_OwnerFacingWindow_OrNull(Sessions.OrchestrationSession.IOrchestrationSession session);

    /// <summary>Brings that window to the front. FALSE when it could not be found or not be raised.</summary>
    bool Try_Focus(string titleFragment);

    /// <summary>
    /// Photographs that window into <paramref name="imagePath"/>. Returns null on success, or the
    /// REASON as one line the owner can read — a screenshot that silently produces no file is
    /// indistinguishable from an app that ignored the request.
    /// </summary>
    Task<string?> Try_Capture_Async(string titleFragment, string imagePath, CancellationToken cancellationToken);

    /// <summary>Tiles this orchestration's terminals. Returns how many were placed.</summary>
    int Organize(Sessions.OrchestrationSession.IOrchestrationSession session);

    /// <summary>Tiles one main terminal per open orchestration. Returns how many were placed.</summary>
    int Organize_MainWindows(IReadOnlyList<Sessions.OrchestrationSession.IOrchestrationSession> openSessions);
}
