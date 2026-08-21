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
    /// WHICH WINDOW IS THIS ORCHESTRATION'S MAIN ONE, in preference order and WITHOUT asking whether
    /// any of them is on screen. The supervisor of a crew, the solo of a basic one — the session the
    /// owner actually talks to, never a communicator, an implementer or a reviewer.
    ///
    /// SPLIT OUT FROM <see cref="Find_OwnerFacingWindow_OrNull"/> so the CHOICE can be pinned by the
    /// suite while the LOOKUP stays where it has to be, behind a Win32 call no test can make. The
    /// order is the whole content of the rule and it survives untouched: supervisor first, then any
    /// live solo, first one found wins.
    ///
    /// One list, two callers — /show focuses the first of these that exists, /organize_mains tiles
    /// one per orchestration. A second copy of "which one is the main window" is exactly the drift
    /// CLAUDE.md decision 12 records the cost of.
    /// </summary>
    public static IReadOnlyList<string> Build_MainWindowCandidates(IOrchestrationSession session)
    {
        List<string> candidates = [];

        if (!OrchestrationShape.Is_BasicOrchestration(session.SupervisorSpawnedUtc))
            candidates.Add(SessionWindowTitle_Builder.Build_ForSupervisor(session.OrchId));

        foreach (var member in session.Members)
        {
            if (member.ClosedUtc != null || MemberKind_Ids.Resolve_Kind(member.MemberId) != MemberKinds.Solo)
                continue;

            candidates.Add(SessionWindowTitle_Builder.Build_ForMember(member.MemberId, session.OrchId));
        }

        return candidates;
    }

    /// <summary>
    /// The session the OWNER talks to — the solo in a basic orchestration, the supervisor otherwise.
    /// Null when its window is not on screen, which the caller must report rather than pretend.
    /// </summary>
    public static string? Find_OwnerFacingWindow_OrNull(IOrchestrationSession session)
    {
        foreach (var candidate in Build_MainWindowCandidates(session))
        {
            if (TerminalWindow_Focuser.Exists_ByTitleFragment(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>Tiles the windows that exist. Returns how many were placed — 0 means none were found.</summary>
    public static int Organize(IOrchestrationSession session)
    {
        return Tile(Build_TitleFragments(session).Where(TerminalWindow_Focuser.Exists_ByTitleFragment).ToList());
    }

    /// <summary>
    /// ONE MAIN WINDOW PER ORCHESTRATION, TILED TOGETHER — the owner's /organize_mains, 2026-08-21:
    /// *"does the terminal organization but for all sups and solos (no general sup)"*.
    ///
    /// /organize answers "show me everything in THIS orchestration"; this answers "show me every
    /// orchestration I am talking to". So it takes exactly the window /show would focus, once per
    /// session, through the same <see cref="Build_MainWindowCandidates"/> rather than forming a
    /// second opinion about which window is the main one.
    ///
    /// EXCLUDING GENERAL IS THE CALLER'S JOB, deliberately: this tiles the sessions it is handed.
    /// General has no session.json and so is not one of them, but a rule stated in two places is a
    /// rule that drifts, and the caller is where "which orchestrations count" already lives.
    ///
    /// A session whose window is not on screen contributes NOTHING rather than reserving an empty
    /// tile — the phantom-tile defect the owner reported against /organize on 2026-08-20, which this
    /// layout must never learn again.
    /// </summary>
    public static int Organize_MainWindows(IReadOnlyList<IOrchestrationSession> sessions)
    {
        List<string> living = [];

        foreach (var session in sessions)
        {
            var main = Find_OwnerFacingWindow_OrNull(session);

            if (main != null)
                living.Add(main);
        }

        return Tile(living);
    }

    /// <summary>
    /// The placement itself, shared by both entry points so the tiling, the ordering and the
    /// raise-after-place cannot drift between them. That last detail is the kind a second copy
    /// loses first, and it is the difference between tiled windows and tiled buried ones.
    /// </summary>
    static int Tile(IReadOnlyList<string> living)
    {
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
