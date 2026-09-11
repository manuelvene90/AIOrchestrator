namespace AIOrchestratorCoreLib.Hosting.HostWindowing;

/// <summary>
/// The Windows implementation: a thin pass-through to the existing <c>WindowFocus/</c> statics.
///
/// <para>
/// DELIBERATELY THIN, AND DELIBERATELY NOT A REWRITE. The P/Invoke, the DWM frame maths, the
/// minimise-restore dance around a capture and the queueing of screenshots all stay where they are
/// and keep their own reasoning; what this class adds is the one thing they could not have — an
/// answer to "is this host even capable of it".
/// </para>
/// </summary>
internal sealed class WindowsHostWindowingModel : IHostWindowing
{
    public bool Is_Supported => true;

    public string? Find_OwnerFacingWindow_OrNull(Sessions.OrchestrationSession.IOrchestrationSession session)
    {
        return WindowFocus.SessionWindows_Organizer.Find_OwnerFacingWindow_OrNull(session);
    }

    public bool Try_Focus(string titleFragment)
    {
        return WindowFocus.TerminalWindow_Focuser.Try_Focus_ByTitleFragment(titleFragment);
    }

    public Task<string?> Try_Capture_Async(string titleFragment, string imagePath, CancellationToken cancellationToken)
    {
        return WindowFocus.TerminalWindow_Capturer.Try_CaptureSessionWindow_Async(titleFragment, imagePath, cancellationToken);
    }

    public int Organize(Sessions.OrchestrationSession.IOrchestrationSession session)
    {
        return WindowFocus.SessionWindows_Organizer.Organize(session);
    }

    public int Organize_MainWindows(IReadOnlyList<Sessions.OrchestrationSession.IOrchestrationSession> openSessions)
    {
        return WindowFocus.SessionWindows_Organizer.Organize_MainWindows(openSessions);
    }
}
