using System.Diagnostics;
using AIOrchestratorCoreLib.Running.ProcessTree;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running.ProcessTree;

/// <summary>
/// "Does this turn have a command running below it" — the sign of life the silence brake reads
/// before it kills a quiet turn. MEASURED 2026-09-11 on the Linux VPS: an idle <c>claude -p</c> turn
/// has zero descendants, a working one has a <c>bash</c> with a <c>node --test</c> under it.
///
/// <para>
/// The parsers and the walk are pure and pinned here on every OS. The one real reading runs only
/// on Linux and macOS, and only spawns three short <c>sleep</c>s (this machine runs production
/// sessions). The Windows reading has no test at all — nothing here can execute it — and the skip
/// does not pretend otherwise.
/// </para>
/// </summary>
public class ProcessDescendantsReaderTests
{
    sealed class RequiresUnixProcessTableFactAttribute : FactAttribute
    {
        public RequiresUnixProcessTableFactAttribute()
        {
            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
                Skip = "Reads the real process table through /proc (Linux) or ps (macOS); the Windows Toolhelp path cannot be driven by /bin/sh and is untested.";
        }
    }

    static (int Pid, int ParentPid, bool IsZombie) Live(int pid, int parentPid) => (pid, parentPid, false);

    static (int Pid, int ParentPid, bool IsZombie) Zombie(int pid, int parentPid) => (pid, parentPid, true);

    // ---- /proc/<pid>/stat ------------------------------------------------------------------

    [Fact]
    public void Parse_ProcStatLine_NormalLine_ReadsPidParentAndLiveState()
    {
        var entry = ProcessDescendants_Reader.Parse_ProcStatLine_OrNull("209048 (cat) R 209040 209048 209040 0 -1 4194304 94 0 0 0");

        Assert.Equal((209048, 209040, false), entry);
    }

    [Fact]
    public void Parse_ProcStatLine_CommWithSpacesAndParentheses_ReadsFieldsAfterTheLastParenthesis()
    {
        var entry = ProcessDescendants_Reader.Parse_ProcStatLine_OrNull("1234 (my (weird) proc) S 1 1234 1234 0 -1");

        Assert.Equal((1234, 1, false), entry);
    }

    [Fact]
    public void Parse_ProcStatLine_CommThatLooksLikeAZombieState_IsNotReadAsOne()
    {
        // A name built to fool a naive split: "Z 99" inside the parentheses must not become state and parent.
        var entry = ProcessDescendants_Reader.Parse_ProcStatLine_OrNull("77 (x) Z 99) S 5 77 77 0");

        Assert.Equal((77, 5, false), entry);
    }

    [Fact]
    public void Parse_ProcStatLine_ZombieLine_IsMarkedZombie()
    {
        var entry = ProcessDescendants_Reader.Parse_ProcStatLine_OrNull("4321 (sleep) Z 4300 4300 4300 0 -1");

        Assert.Equal((4321, 4300, true), entry);
    }

    [Fact]
    public void Parse_ProcStatLine_Garbage_ReturnsNull()
    {
        Assert.Null(ProcessDescendants_Reader.Parse_ProcStatLine_OrNull(""));
        Assert.Null(ProcessDescendants_Reader.Parse_ProcStatLine_OrNull("not a stat line"));
        Assert.Null(ProcessDescendants_Reader.Parse_ProcStatLine_OrNull("(cat) R 1"));
        Assert.Null(ProcessDescendants_Reader.Parse_ProcStatLine_OrNull("12 (cat)"));
        Assert.Null(ProcessDescendants_Reader.Parse_ProcStatLine_OrNull("12 (cat) R notanumber"));
        Assert.Null(ProcessDescendants_Reader.Parse_ProcStatLine_OrNull("abc (cat) R 1"));
    }

    // ---- ps -A -o pid=,ppid=,stat= (macOS) ---------------------------------------------------

    [Fact]
    public void Parse_PsLine_PaddedLiveAndZombieLines_ReadAsTheirState()
    {
        Assert.Equal((123, 1, false), ProcessDescendants_Reader.Parse_PsLine_OrNull("  123     1 Ss  "));
        Assert.Equal((456, 123, true), ProcessDescendants_Reader.Parse_PsLine_OrNull("  456   123 Z+"));
    }

    [Fact]
    public void Parse_PsLine_Garbage_ReturnsNull()
    {
        Assert.Null(ProcessDescendants_Reader.Parse_PsLine_OrNull(""));
        Assert.Null(ProcessDescendants_Reader.Parse_PsLine_OrNull("PID PPID STAT"));
        Assert.Null(ProcessDescendants_Reader.Parse_PsLine_OrNull("123 1"));
        Assert.Null(ProcessDescendants_Reader.Parse_PsLine_OrNull("123 1 S extra"));
    }

    // ---- the walk ------------------------------------------------------------------------

    [Fact]
    public void Count_Descendants_RootWithNoChildren_ReturnsZero()
    {
        var table = new[] { Live(1, 0), Live(100, 1), Live(200, 1) };

        Assert.Equal(0, ProcessDescendants_Reader.Count_Descendants(table, 100));
    }

    [Fact]
    public void Count_Descendants_ChainOfThree_CountsAllThree()
    {
        var table = new[] { Live(100, 1), Live(101, 100), Live(102, 101), Live(103, 102), Live(500, 1) };

        Assert.Equal(3, ProcessDescendants_Reader.Count_Descendants(table, 100));
    }

    [Fact]
    public void Count_Descendants_SiblingsAndTheirChildren_AreAllCounted()
    {
        var table = new[] { Live(100, 1), Live(101, 100), Live(102, 100), Live(103, 102), Live(500, 1), Live(501, 500) };

        Assert.Equal(3, ProcessDescendants_Reader.Count_Descendants(table, 100));
    }

    [Fact]
    public void Count_Descendants_ZombieChild_IsNeitherCountedNorWalkedThrough()
    {
        var table = new[] { Live(100, 1), Zombie(101, 100), Live(102, 101), Live(103, 100) };

        Assert.Equal(1, ProcessDescendants_Reader.Count_Descendants(table, 100));
    }

    [Fact]
    public void Count_Descendants_Cycle_TerminatesAndNeverCountsTheRoot()
    {
        // Pid reuse can make the root a child of its own grandchild; the walk must end, and count 2.
        var table = new[] { Live(100, 102), Live(101, 100), Live(102, 101), Live(7, 7) };

        Assert.Equal(2, ProcessDescendants_Reader.Count_Descendants(table, 100));
        Assert.Equal(0, ProcessDescendants_Reader.Count_Descendants(table, 7));
    }

    [Fact]
    public void Count_Descendants_RootAbsentOrZombie_ReturnsZeroEvenIfEntriesStillNameIt()
    {
        // Windows keeps a dead parent's pid on its orphans: a finished turn must not own them.
        var orphans = new[] { Live(101, 100), Live(102, 101) };
        var zombieRoot = new[] { Zombie(100, 1), Live(101, 100) };

        Assert.Equal(0, ProcessDescendants_Reader.Count_Descendants(orphans, 100));
        Assert.Equal(0, ProcessDescendants_Reader.Count_Descendants(zombieRoot, 100));
    }

    [Fact]
    public void Count_Descendants_OrNull_NonPositivePid_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProcessDescendants_Reader.Count_Descendants_OrNull(0));
    }

    // ---- the real process table ----------------------------------------------------------

    [RequiresUnixProcessTableFact]
    public void Count_Descendants_OrNull_PidThatDoesNotExist_ReturnsZero()
    {
        Assert.Equal(0, ProcessDescendants_Reader.Count_Descendants_OrNull(int.MaxValue - 7));
    }

    [RequiresUnixProcessTableFact]
    public void Count_Descendants_OrNull_ShellWithTwoBackgroundSleeps_CountsThem_AndALoneSleepCountsNone()
    {
        Process? shell = null;
        Process? lone = null;

        try
        {
            shell = Start("/bin/sh", "-c", "sleep 30 & sleep 30 & wait");

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            int? count = ProcessDescendants_Reader.Count_Descendants_OrNull(shell.Id);
            while ((count == null || count < 2) && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(100);
                count = ProcessDescendants_Reader.Count_Descendants_OrNull(shell.Id);
            }

            Assert.NotNull(count);
            Assert.True(count >= 2, $"expected the shell's two background sleeps below it, read {count}");

            var shellPid = shell.Id;
            shell.Kill(entireProcessTree: true);
            shell.WaitForExit();
            Assert.Equal(0, ProcessDescendants_Reader.Count_Descendants_OrNull(shellPid));

            lone = Start("sleep", "30");
            Assert.Equal(0, ProcessDescendants_Reader.Count_Descendants_OrNull(lone.Id));
        }
        finally
        {
            Kill_Quietly(shell);
            Kill_Quietly(lone);
        }
    }

    static Process Start(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName) { UseShellExecute = false };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return Process.Start(startInfo) ?? throw new InvalidOperationException($"could not start {fileName}");
    }

    static void Kill_Quietly(Process? process)
    {
        if (process == null)
            return;

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
            }
        }
        catch (Exception)
        {
            // Cleanup only: the process is already gone, and the test's verdict was decided above.
        }
        finally
        {
            process.Dispose();
        }
    }
}
