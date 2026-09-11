namespace AIOrchestratorCoreLib.Hosting.HostWindowing;

/// <summary>
/// Linux and macOS, until real implementations exist: every answer is "no", and NOTHING THROWS.
///
/// <para>
/// THE POINT IS THE ABSENCE OF AN EXCEPTION. A `DllNotFoundException` from `user32.dll` on the Linux
/// daemon escaped the command dispatch and the whole inbound batch with it, and Telegram re-served
/// every update in that batch — four times, on 2026-09-08. The refusal the owner reads is composed
/// by the caller (it knows which command they typed); what happens here is simply that the call
/// returns.
/// </para>
/// <para>
/// IT IS NOT A STUB WAITING TO BE FILLED IN BY THIS CLASS. A real macOS implementation is a
/// different mechanism entirely — AppleScript or the Accessibility API, not P/Invoke — so it will be
/// its own model beside this one, and this one will keep being the honest answer for hosts that have
/// no window server at all.
/// </para>
/// </summary>
internal sealed class UnsupportedHostWindowingModel : IHostWindowing
{
    public bool Is_Supported => false;

    public string? Find_OwnerFacingWindow_OrNull(Sessions.OrchestrationSession.IOrchestrationSession session)
    {
        return null;
    }

    public bool Try_Focus(string titleFragment)
    {
        return false;
    }

    public Task<string?> Try_Capture_Async(string titleFragment, string imagePath, CancellationToken cancellationToken)
    {
        return Task.FromResult<string?>("this host cannot take screenshots");
    }

    public int Organize(Sessions.OrchestrationSession.IOrchestrationSession session)
    {
        return 0;
    }

    public int Organize_MainWindows(IReadOnlyList<Sessions.OrchestrationSession.IOrchestrationSession> openSessions)
    {
        return 0;
    }
}
