using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace AIOrchestratorCoreLib.Running.ProcessTree;

/// <summary>
/// DOES A TURN HAVE WORK RUNNING BELOW IT — the count of live processes anywhere under a root pid,
/// read from the operating system's own process table. One reading, no policy: the silence brake
/// decides what a count means.
///
/// <para>
/// WHY THIS IS A SIGN OF LIFE. Each work turn is a <c>claude -p</c> child of the app, and a turn
/// that is building or running a test suite prints nothing for many minutes while a command runs
/// under it. MEASURED 2026-09-11 on the Linux VPS: an idle <c>claude -p</c> turn had ZERO descendant
/// processes; a working one had <c>/bin/bash -c source ~/.claude/shell-snapshots/...</c> with
/// <c>node --test ...</c> under it. So "any live descendant" is the signal, and the reader only has
/// to count — it never has to recognise what the command is.
/// </para>
/// <para>
/// NULL MEANS "CANNOT TELL", AND THE CALLER READS "CANNOT TELL" AS ALIVE. A null here is never "no
/// work": it is an OS this reader does not know, a <c>ps</c> that timed out, a table line nobody
/// has seen, an exception. The brake must treat it as a sign of life, because the two errors are
/// not symmetric — reading a live build as idle kills an hour of work, reading an idle turn as busy
/// only delays the brake to its next reason. Every ambiguity in this file is resolved in that same
/// direction: it may over-count, it must not under-count.
/// </para>
/// <para>
/// ZOMBIES ARE NOT LIFE, and are neither counted nor walked through. A zombie (Linux state
/// <c>Z</c>/<c>X</c>, macOS stat starting with <c>Z</c>) has already exited and is only waiting to
/// be reaped; its own children were reparented by the kernel when it died, so walking through it
/// would find nothing that still belongs to the turn. A ROOT that is absent from the table, or is
/// itself a zombie, answers 0: the turn has ended, and whatever it left behind is not its work.
/// </para>
/// <para>
/// THREE OS, runtime checks only (no <c>#if</c> — the daemon is first-class on all three). Linux
/// reads <c>/proc/&lt;pid&gt;/stat</c>; macOS asks <c>ps</c>; Windows walks a Toolhelp snapshot.
/// The Windows path was written 2026-09-11 on a Linux box and has NEVER RUN — see
/// <see cref="Read_WindowsTable_OrNull"/> for what it does about pid reuse and what it cannot.
/// </para>
/// </summary>
public static class ProcessDescendants_Reader
{
    /// <summary>
    /// How long <c>ps</c> may take on macOS before the reading is abandoned as "cannot tell". It
    /// answers in milliseconds on a normal machine; five seconds only exists so a wedged <c>ps</c>
    /// cannot hang the brake's loop.
    /// </summary>
    public static readonly TimeSpan PS_TIMEOUT = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The number of LIVE processes anywhere below <paramref name="rootPid"/> — children,
    /// grandchildren, and so on — not counting the root itself. 0 when there are none, or when the
    /// root does not exist (a finished turn has no work below it). Null ONLY when this platform or
    /// its permissions give no way to tell; the caller treats null as "cannot tell", and the silence
    /// brake reads "cannot tell" as ALIVE — the safe direction is never killing live work.
    /// </summary>
    public static int? Count_Descendants_OrNull(int rootPid)
    {
        if (rootPid <= 0)
            throw new ArgumentOutOfRangeException(nameof(rootPid), rootPid, "a process id is positive — 0 would name the kernel and count every process on the machine");

        try
        {
            if (OperatingSystem.IsLinux())
            {
                var linuxTable = Read_LinuxTable_OrNull();
                return linuxTable == null ? null : Count_Descendants(linuxTable, rootPid);
            }

            if (OperatingSystem.IsMacOS())
            {
                var macTable = Read_MacTable_OrNull();
                return macTable == null ? null : Count_Descendants(macTable, rootPid);
            }

            if (OperatingSystem.IsWindows())
            {
                var windowsTable = Read_WindowsTable_OrNull();
                if (windowsTable == null)
                    return null;

                var creationTimes = new Dictionary<int, long?>();
                return Count_Descendants_Core(windowsTable, rootPid, (child, parent) => Is_PlausibleWindowsEdge(child, parent, creationTimes));
            }

            // An OS nobody has measured: say so rather than guess its process table.
            return null;
        }
        catch (Exception)
        {
            // Swallowed on purpose: a reading that failed is "cannot tell", which the brake reads as
            // alive. Propagating would take the brake's loop down over a question it can live without.
            return null;
        }
    }

    /// <summary>
    /// One line of <c>/proc/&lt;pid&gt;/stat</c> read for the three fields the tree needs, or null
    /// when the line is not that shape.
    ///
    /// <para>
    /// THE COMM FIELD IS THE TRAP. The second field is the executable name in parentheses, and the
    /// kernel puts it there verbatim: it may contain spaces and <c>)</c> itself —
    /// <c>1234 (my (weird) proc) S 1 ...</c> is a legal line. Splitting on spaces reads the state out
    /// of the name, so the fields are taken from the text after the LAST <c>)</c>, which the name
    /// cannot move: state is the first of them, parent pid the second.
    /// </para>
    /// <para>
    /// <c>Z</c> (zombie) and <c>X</c> (dead) both read as exited: neither is running anything.
    /// </para>
    /// </summary>
    public static (int Pid, int ParentPid, bool IsZombie)? Parse_ProcStatLine_OrNull(string statLine)
    {
        if (string.IsNullOrWhiteSpace(statLine))
            return null;

        var open = statLine.IndexOf('(');
        var close = statLine.LastIndexOf(')');
        if (open <= 0 || close < open)
            return null;

        if (!int.TryParse(statLine.AsSpan(0, open).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0)
            return null;

        var after = statLine[(close + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (after.Length < 2 || after[0].Length != 1)
            return null;

        if (!int.TryParse(after[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parentPid))
            return null;

        var state = after[0][0];
        return (pid, parentPid, state == 'Z' || state == 'X');
    }

    /// <summary>
    /// One line of <c>ps -A -o pid=,ppid=,stat=</c> (macOS), or null when it is not three fields
    /// with two numbers first. A stat starting with <c>Z</c> is a zombie; the letters after it
    /// (<c>s</c>, <c>+</c>, <c>&lt;</c> …) only qualify the state and are ignored.
    /// </summary>
    public static (int Pid, int ParentPid, bool IsZombie)? Parse_PsLine_OrNull(string psLine)
    {
        if (string.IsNullOrWhiteSpace(psLine))
            return null;

        var fields = psLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 3)
            return null;

        if (!int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0)
            return null;

        if (!int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parentPid))
            return null;

        return (pid, parentPid, fields[2].StartsWith('Z'));
    }

    /// <summary>
    /// The live processes below <paramref name="rootPid"/> in a process table, found breadth-first.
    /// Pure, so the rules are pinned on every OS rather than only where a real table can be read.
    ///
    /// <para>
    /// A ROOT MISSING FROM THE TABLE, OR A ZOMBIE ROOT, ANSWERS 0 — the turn has ended. This matters
    /// most on Windows, where a child keeps naming its dead parent's pid forever: without the check,
    /// an orphan would still be counted as the finished turn's work.
    /// </para>
    /// <para>
    /// CYCLE-SAFE. Every pid is visited once, and the root is visited first, so a table in which pid
    /// reuse has made a process its own ancestor (or the root a child of its own child) terminates
    /// and never counts the root. A pid listed as its own parent (Windows lists the idle process that
    /// way) is not an edge.
    /// </para>
    /// </summary>
    public static int Count_Descendants(IReadOnlyList<(int Pid, int ParentPid, bool IsZombie)> table, int rootPid)
    {
        return Count_Descendants_Core(table, rootPid, isPlausibleEdge: null);
    }

    /// <summary>
    /// The walk itself, with an optional veto on a single parent→child edge — used only by Windows
    /// to drop an edge that pid reuse has made up. The veto can only REMOVE an edge the table
    /// asserts, so a veto that cannot decide must answer true (keep it): an edge kept wrongly
    /// over-counts, which the brake survives; an edge dropped wrongly under-counts, which kills work.
    /// </summary>
    static int Count_Descendants_Core(IReadOnlyList<(int Pid, int ParentPid, bool IsZombie)> table, int rootPid, Func<int, int, bool>? isPlausibleEdge)
    {
        var rootIsLive = false;
        var childrenByParent = new Dictionary<int, List<int>>();

        foreach (var (pid, parentPid, isZombie) in table)
        {
            if (isZombie)
                continue;

            if (pid == rootPid)
                rootIsLive = true;

            if (pid == parentPid)
                continue;

            if (!childrenByParent.TryGetValue(parentPid, out var children))
            {
                children = [];
                childrenByParent[parentPid] = children;
            }

            children.Add(pid);
        }

        if (!rootIsLive)
            return 0;

        var visited = new HashSet<int> { rootPid };
        var queue = new Queue<int>();
        queue.Enqueue(rootPid);
        var count = 0;

        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            if (!childrenByParent.TryGetValue(parent, out var children))
                continue;

            foreach (var child in children)
            {
                if (visited.Contains(child))
                    continue;

                if (isPlausibleEdge != null && !isPlausibleEdge(child, parent))
                    continue;

                visited.Add(child);
                count++;
                queue.Enqueue(child);
            }
        }

        return count;
    }

    /// <summary>
    /// Every process in <c>/proc</c>, or null when a stat line is not the shape the kernel documents.
    ///
    /// <para>
    /// A PROCESS THAT VANISHES BETWEEN THE LISTING AND THE READ IS SKIPPED — that is the ordinary
    /// race of reading a live table, not an error, and a process that has gone is not work. A stat we
    /// are DENIED is skipped too: every process a turn spawns runs as the app's own user and its stat
    /// is always readable to it (<c>hidepid</c> hides other users' entries from the listing rather
    /// than denying them), so a denial names a process that is not the turn's. A stat that reads but
    /// does not PARSE is different — it may be exactly the process we are looking for — so it turns
    /// the whole reading into "cannot tell".
    /// </para>
    /// </summary>
    static List<(int Pid, int ParentPid, bool IsZombie)>? Read_LinuxTable_OrNull()
    {
        var table = new List<(int Pid, int ParentPid, bool IsZombie)>();

        foreach (var directory in new DirectoryInfo("/proc").EnumerateDirectories())
        {
            if (!int.TryParse(directory.Name, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                continue;

            string statLine;
            try
            {
                statLine = File.ReadAllText(Path.Combine(directory.FullName, "stat"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (statLine.Length == 0)
                continue;

            var entry = Parse_ProcStatLine_OrNull(statLine.TrimEnd('\n'));
            if (entry == null)
                return null;

            table.Add(entry.Value);
        }

        return table;
    }

    /// <summary>
    /// Every process <c>ps</c> reports, or null when it fails, times out (killed, see
    /// <see cref="PS_TIMEOUT"/>), exits non-zero, or prints a line that is not the asked-for shape —
    /// a line skipped might be the one descendant that matters. No shell: arguments go through
    /// <see cref="ProcessStartInfo.ArgumentList"/>.
    /// </summary>
    static List<(int Pid, int ParentPid, bool IsZombie)>? Read_MacTable_OrNull()
    {
        var startInfo = new ProcessStartInfo("ps")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-A");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("pid=,ppid=,stat=");

        using var process = Process.Start(startInfo);
        if (process == null)
            return null;

        // Both pipes drained concurrently: a full stderr pipe nobody reads would wedge ps until the timeout.
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(PS_TIMEOUT))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception)
            {
                // It exited between the timeout and the kill; the reading is abandoned either way.
            }

            return null;
        }

        if (process.ExitCode != 0)
            return null;

        var output = standardOutput.GetAwaiter().GetResult();
        _ = standardError.GetAwaiter().GetResult();

        var table = new List<(int Pid, int ParentPid, bool IsZombie)>();
        foreach (var line in output.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var entry = Parse_PsLine_OrNull(line);
            if (entry == null)
                return null;

            table.Add(entry.Value);
        }

        return table;
    }

    /// <summary>
    /// Every process in a Toolhelp snapshot, or null when the snapshot cannot be taken or the walk
    /// stops for any reason other than reaching its end (a truncated table could miss the
    /// descendant that matters). Toolhelp lists running processes only, so nothing is a zombie.
    ///
    /// <para>
    /// UNTESTED: written 2026-09-11 on a Linux machine; no test in the suite has ever executed it.
    /// </para>
    /// <para>
    /// PID REUSE. Windows never updates a child's recorded parent pid, so once a parent exits its pid
    /// can be recycled and an unrelated old process appears to be the new owner's child. The
    /// mitigation is <see cref="Is_PlausibleWindowsEdge"/>: a child created BEFORE its supposed
    /// parent cannot be its child, and that edge is dropped. It needs each process's creation time,
    /// which needs a handle, and a handle can be refused (protected and other-session processes);
    /// then the edge is KEPT, so the limitation that remains over-counts — a stray orphan can make an
    /// idle turn look busy, delaying the brake — and never under-counts.
    /// </para>
    /// </summary>
    static List<(int Pid, int ParentPid, bool IsZombie)>? Read_WindowsTable_OrNull()
    {
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == INVALID_HANDLE_VALUE || snapshot == IntPtr.Zero)
            return null;

        try
        {
            var table = new List<(int Pid, int ParentPid, bool IsZombie)>();
            var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };

            var more = Process32FirstW(snapshot, ref entry);
            while (more)
            {
                table.Add(((int)entry.th32ProcessID, (int)entry.th32ParentProcessID, false));
                more = Process32NextW(snapshot, ref entry);
            }

            return Marshal.GetLastWin32Error() == ERROR_NO_MORE_FILES ? table : null;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    /// <summary>
    /// False only when both creation times were read and the child is older than its supposed
    /// parent — the one case that proves pid reuse. Anything unreadable keeps the edge (see
    /// <see cref="Count_Descendants_Core"/> for why a veto that cannot decide must not remove).
    /// </summary>
    static bool Is_PlausibleWindowsEdge(int childPid, int parentPid, Dictionary<int, long?> creationTimes)
    {
        var child = Read_WindowsCreationTime_OrNull(childPid, creationTimes);
        var parent = Read_WindowsCreationTime_OrNull(parentPid, creationTimes);

        return child == null || parent == null || child.Value >= parent.Value;
    }

    /// <summary>A process's creation time as a FILETIME, cached per reading, or null when the process cannot be opened.</summary>
    static long? Read_WindowsCreationTime_OrNull(int pid, Dictionary<int, long?> creationTimes)
    {
        if (creationTimes.TryGetValue(pid, out var cached))
            return cached;

        long? creation = null;
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle != IntPtr.Zero)
        {
            try
            {
                if (GetProcessTimes(handle, out var created, out _, out _, out _))
                    creation = created;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        creationTimes[pid] = creation;
        return creation;
    }

    const uint TH32CS_SNAPPROCESS = 0x00000002;
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    const int ERROR_NO_MORE_FILES = 18;
    static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string? szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")]
    static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")]
    static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetProcessTimes(IntPtr hProcess, out long lpCreationTime, out long lpExitTime, out long lpKernelTime, out long lpUserTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr hObject);
}
