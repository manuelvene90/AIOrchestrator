using System.Runtime.InteropServices;
using AIOrchestratorCoreLib.Layout;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Spawning;

namespace AIOrchestratorCoreLib.WindowFocus;

/// <summary>
/// Brings one orchestration's terminals to the front, and tiles them across the screen.
///
/// IT LIVES IN THE LIBRARY, NOT THE WINDOW, because the owner asked for these from their PHONE
/// (2026-08-20): "/Show should bring the solo or sup in front of the screen, and /organize should
/// trigger like pressing the organize button in the app". The app's button had the logic inside a
/// WPF click handler, where the Telegram bridge cannot reach it — so it moved here and the button
/// now calls this too. One implementation, or the phone and the button drift apart.
///
/// CLOSED MEMBERS ARE NEVER INCLUDED, and neither is a supervisor that does not exist: a basic
/// orchestration has no SUP window, and asking for one used to put a phantom in the list. The
/// layout is then computed from the windows that ACTUALLY EXIST, so a session whose terminal is
/// gone cannot take a tile and leave a hole.
/// </summary>
public static class SessionWindows_Organizer
{
    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    const uint SPI_GETWORKAREA = 0x0030;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SystemParametersInfo(uint action, uint param, ref RECT rect, uint update);

    /// <summary>
    /// Every window this orchestration could have, newest role first. A supervisor entry is only
    /// offered when one was actually spawned — OrchestrationShape knows the difference.
    /// </summary>
    public static IReadOnlyList<string> Build_TitleFragments(IOrchestrationSession session)
    {
        List<string> fragments = [];

        if (!OrchestrationShape.Is_BasicOrchestration(session.SupervisorSpawnedUtc))
            fragments.Add(SessionWindowTitle_Builder.Build_ForSupervisor(session.OrchId));

        if (session.CommunicatorSpawnedUtc != null)
            fragments.Add(SessionWindowTitle_Builder.Build_ForCommunicator(session.OrchId));

        foreach (var member in session.Members)
        {
            if (member.ClosedUtc == null)
                fragments.Add(SessionWindowTitle_Builder.Build_ForMember(member.MemberId, session.OrchId));
        }

        return fragments;
    }

    /// <summary>
    /// The session the OWNER talks to — the solo in a basic orchestration, the supervisor otherwise.
    /// Null when its window is not on screen, which the caller must report rather than pretend.
    /// </summary>
    public static string? Find_OwnerFacingWindow_OrNull(IOrchestrationSession session)
    {
        var supervisor = SessionWindowTitle_Builder.Build_ForSupervisor(session.OrchId);

        if (!OrchestrationShape.Is_BasicOrchestration(session.SupervisorSpawnedUtc)
            && TerminalWindow_Focuser.Exists_ByTitleFragment(supervisor))
            return supervisor;

        foreach (var member in session.Members)
        {
            if (member.ClosedUtc != null || MemberKind_Ids.Resolve_Kind(member.MemberId) != MemberKinds.Solo)
                continue;

            var solo = SessionWindowTitle_Builder.Build_ForMember(member.MemberId, session.OrchId);

            if (TerminalWindow_Focuser.Exists_ByTitleFragment(solo))
                return solo;
        }

        return null;
    }

    /// <summary>Tiles the windows that exist. Returns how many were placed — 0 means none were found.</summary>
    public static int Organize(IOrchestrationSession session)
    {
        var living = Build_TitleFragments(session)
            .Where(TerminalWindow_Focuser.Exists_ByTitleFragment)
            .ToList();

        if (living.Count == 0)
            return 0;

        var area = Get_WorkArea();

        var tiles = TileLayout_Calculator.Build_Tiles(
            living.Count, area.Left, area.Top, area.Right - area.Left, area.Bottom - area.Top);

        var placed = 0;

        for (var i = 0; i < living.Count && i < tiles.Count; i++)
        {
            TerminalWindow_Focuser.Try_PlaceWindow_ByTitleFragment(living[i], tiles[i].X, tiles[i].Y, tiles[i].Width, tiles[i].Height);

            // Raised AFTER placing, or they end up tiled and buried.
            TerminalWindow_Focuser.Try_Focus_ByTitleFragment(living[i]);

            placed++;
        }

        return placed;
    }

    /// <summary>
    /// The desktop minus the taskbar. Falls back to a plain 1920x1080 if Windows refuses to say,
    /// because a failed query must not put every terminal at 0x0.
    /// </summary>
    static RECT Get_WorkArea()
    {
        var rect = new RECT();

        if (SystemParametersInfo(SPI_GETWORKAREA, 0, ref rect, 0) && rect.Right > rect.Left && rect.Bottom > rect.Top)
            return rect;

        return new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
    }
}
