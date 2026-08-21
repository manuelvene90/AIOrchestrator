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

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("dwmapi.dll")]
    static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, ref RECT value, int size);

    /// <summary>DWMWA_EXTENDED_FRAME_BOUNDS — the window's VISIBLE rectangle, shadow excluded.</summary>
    const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    const uint WM_CLOSE = 0x0010;
    const uint SWP_NOZORDER = 0x0004;
    const uint SWP_SHOWWINDOW = 0x0040;

    /// <summary>
    /// Moves and resizes a session's terminal to an exact rectangle (the "Organize" tiling), and
    /// brings it up. Returns false when no window carries the fragment — a session that is not
    /// running is simply skipped by the caller.
    /// </summary>
    /// <summary>
    /// Does a window with this fragment EXIST? Deliberately separate from focusing: Windows refuses
    /// SetForegroundWindow in several ordinary situations, so treating focus failure as "no window"
    /// made the tiler mis-count what it was about to lay out.
    /// </summary>
    public static bool Exists_ByTitleFragment(string titleFragment)
    {
        return Find_WindowHandle_ByTitleFragment(titleFragment) != IntPtr.Zero;
    }

    public static bool Try_PlaceWindow_ByTitleFragment(string titleFragment, int x, int y, int width, int height)
    {
        var foundHandle = Find_WindowHandle_ByTitleFragment(titleFragment);

        if (foundHandle == IntPtr.Zero)
            return false;

        // A minimised window ignores SetWindowPos geometry until it is restored.
        if (IsIconic(foundHandle))
            ShowWindow(foundHandle, SW_RESTORE);

        // A window is BIGGER than it looks: since Vista the resize border and drop shadow live
        // outside the visible frame, so placing tiles at exact coordinates leaves a few pixels of
        // desktop showing between them. The fix is to ask DWM where the window VISUALLY ends and
        // grow the target rectangle by the invisible margin, so the visible edges meet.
        var margin = Get_InvisibleBorder(foundHandle);

        return SetWindowPos(
            foundHandle,
            IntPtr.Zero,
            x - margin.Left,
            y - margin.Top,
            width + margin.Left + margin.Right,
            height + margin.Top + margin.Bottom,
            SWP_NOZORDER | SWP_SHOWWINDOW);
    }

    /// <summary>How far the window rectangle extends beyond what the user can actually see.</summary>
    static (int Left, int Top, int Right, int Bottom) Get_InvisibleBorder(IntPtr windowHandle)
    {
        try
        {
            if (!GetWindowRect(windowHandle, out var outer))
                return (0, 0, 0, 0);

            var visible = new RECT();
            var size = Marshal.SizeOf<RECT>();

            if (DwmGetWindowAttribute(windowHandle, DWMWA_EXTENDED_FRAME_BOUNDS, ref visible, size) != 0)
                return (0, 0, 0, 0);

            return (
                visible.Left - outer.Left,
                visible.Top - outer.Top,
                outer.Right - visible.Right,
                outer.Bottom - visible.Bottom);
        }
        catch
        {
            // Compensation is cosmetic — never let it stop a window being placed.
            return (0, 0, 0, 0);
        }
    }

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

    // THERE IS NO LIVE RENAME HERE, DELIBERATELY, and it is not an omission to be filled in.
    //
    // Try_Rename_ByTitleFragment lived here from 2026-08-06 to 2026-08-21 and called SetWindowText
    // on the wt.exe host window. That call SUCCEEDS — the OS caption really does change, and
    // GetWindowText reads it back — but Windows Terminal draws its titlebar from its own per-tab
    // state and never looks at the caption, so nothing the owner could see ever changed. Verified by
    // spawning a real window with the app's own invocation, renaming it, and screenshotting it: the
    // titlebar still read the original. A "rename" that returns true and does nothing is worse than
    // none, because the log then says it worked.
    //
    // There is no external verb to retitle a running WT tab. The only real route is a process INSIDE
    // that session emitting ESC ]2;...BEL on its own stdout, which the app cannot do from outside and
    // which --suppressApplicationTitle (set at spawn, on purpose) blocks anyway.
    //
    // So the name is carried by the DURABLE path instead: SpawnCommand_Builder composes it into
    // --title at spawn, and a running window picks it up at its next respawn. The owner chose that
    // trade deliberately on 2026-08-21, told what it costs: a terminal keeps its old title until the
    // watchdog or an app restart brings it back.

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

            if (!SessionWindowTitle_Matcher.Matches(buffer.ToString(), titleFragment))
                return true;

            foundHandle = hWnd;
            return false;
        }, IntPtr.Zero);

        return foundHandle;
    }
}
