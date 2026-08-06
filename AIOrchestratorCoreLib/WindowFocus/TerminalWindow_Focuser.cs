using System.Runtime.InteropServices;
using System.Text;

namespace AIOrchestratorCoreLib.WindowFocus;

/// <summary>
/// Brings a session's terminal window to the foreground by TITLE fragment. Sessions spawn in their
/// own Windows Terminal window (wt -w new), whose title is the session title ("SUP · arb-1",
/// "IMP-2 · arb-1", "GENERAL"), so a substring match finds the right window.
/// </summary>
public static class TerminalWindow_Focuser
{
    const int SW_RESTORE = 9;

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool SetWindowText(IntPtr hWnd, string text);

    const uint WM_CLOSE = 0x0010;

    /// <summary>Returns false when no visible window carries the fragment in its title.</summary>
    public static bool Try_Focus_ByTitleFragment(string titleFragment)
    {
        var foundHandle = Find_WindowHandle_ByTitleFragment(titleFragment);

        if (foundHandle == IntPtr.Zero)
            return false;

        if (IsIconic(foundHandle))
            ShowWindow(foundHandle, SW_RESTORE);

        return SetForegroundWindow(foundHandle);
    }

    /// <summary>
    /// Closes a session's terminal window. Windows Terminal keeps a pane open ("press enter to
    /// restart") after its process is KILLED rather than exiting gracefully — so killing a session
    /// tree must be followed by closing its window. With the process already dead, WM_CLOSE closes
    /// the window silently.
    /// </summary>
    public static bool Try_Close_ByTitleFragment(string titleFragment)
    {
        var foundHandle = Find_WindowHandle_ByTitleFragment(titleFragment);

        if (foundHandle == IntPtr.Zero)
            return false;

        return PostMessage(foundHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>
    /// Renames a session's terminal window (used when the orchestration gets its goal name).
    /// The new title must KEEP the original fragment as a prefix so title-based focusing and
    /// closing keep working.
    /// </summary>
    public static bool Try_Rename_ByTitleFragment(string titleFragment, string newTitle)
    {
        if (!newTitle.Contains(titleFragment, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"New title '{newTitle}' must contain the fragment '{titleFragment}' — focusing/closing match on it");

        var foundHandle = Find_WindowHandle_ByTitleFragment(titleFragment);

        if (foundHandle == IntPtr.Zero)
            return false;

        return SetWindowText(foundHandle, newTitle);
    }

    static IntPtr Find_WindowHandle_ByTitleFragment(string titleFragment)
    {
        var foundHandle = IntPtr.Zero;

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
                return true;

            var buffer = new StringBuilder(512);

            if (GetWindowText(hWnd, buffer, buffer.Capacity) <= 0)
                return true;

            if (!buffer.ToString().Contains(titleFragment, StringComparison.OrdinalIgnoreCase))
                return true;

            foundHandle = hWnd;
            return false;
        }, IntPtr.Zero);

        return foundHandle;
    }
}
